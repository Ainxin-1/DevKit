using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DevKit.Core;
using DevKit.Models;
using DevKit.Views;

namespace DevKit.ViewModels;

/// <summary>左侧分类节点</summary>
public class CategoryNode
{
    public required string Title { get; init; }
    public ToolCategory? Category { get; init; }
    public ToolSubcategory? Subcategory { get; init; }

    public override string ToString() => Title;
}

/// <summary>筛选模式</summary>
public enum FilterMode
{
    All,
    Common,
    Uncommon
}

/// <summary>筛选下拉选项</summary>
public class FilterOption
{
    public required FilterMode Mode { get; init; }
    public required string Label { get; init; }
}

/// <summary>
/// 主界面 ViewModel：分类、搜索、筛选、勾选、检测、安装。
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, ToolInfo> _toolIndex;
    private CancellationTokenSource? _cts;

    public MainViewModel()
    {
        _dispatcher = Application.Current.Dispatcher;

        Categories = new ObservableCollection<CategoryNode>
        {
            new() { Title = "开发环境", Category = ToolCategory.Environment, Subcategory = null },
            new() { Title = "包管理器", Category = ToolCategory.PackageManager, Subcategory = null },
        };
        _selectedCategoryNode = Categories[0];

        // 加载配置
        var tools = ConfigLoader.Load();
        _toolIndex = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        DetectCommand = new RelayCommand(_ => _ = DetectAsync());
        InstallCommand = new RelayCommand(_ => _ = InstallAsync(),
            _ => !IsBusy && !IsInstalling && AllDetections.Any(d => d.IsSelected && d.Status == DetectionStatus.NotInstalled));
        UpdateCommand = new RelayCommand(_ => _ = UpdateAsync(),
            _ => !IsBusy && !IsInstalling && AllDetections.Any(d => d.IsSelected && d.Status == DetectionStatus.UpdateAvailable));
        CancelCommand = new RelayCommand(_ => CancelInstall(), _ => IsInstalling);
        ClearSelectionCommand = new RelayCommand(_ => ClearSelection());
        ShowLogCommand = new RelayCommand(_ => ShowLogWindow());
        ShowSystemInfoCommand = new RelayCommand(_ => ShowSystemInfoWindow());
        OpenLogFileCommand = new RelayCommand(_ => OpenLogFile());
        ExportEnvironmentCommand = new RelayCommand(_ => ExportEnvironment());
        ImportEnvironmentCommand = new RelayCommand(_ => ImportEnvironment());
        ScanProjectCommand = new RelayCommand(_ => ScanProject());

        FilterOptions = new ObservableCollection<FilterOption>
        {
            new() { Mode = FilterMode.All, Label = "[全部]" },
            new() { Mode = FilterMode.Common, Label = "[常用]" },
            new() { Mode = FilterMode.Uncommon, Label = "[不常用]" },
        };
        _selectedFilter = FilterOptions[0];

        // 初始占位（避免空 UI）
        foreach (var tool in tools)
        {
            var d = new ToolDetection { Tool = tool, Status = DetectionStatus.Detecting };
            d.PropertyChanged += OnDetectionChanged;
            AllDetections.Add(d);
        }
        RefreshFilter();

        _ = DetectAsync();
    }

    // ---------- 集合 ----------
    public ObservableCollection<CategoryNode> Categories { get; }
    public ObservableCollection<ToolDetection> AllDetections { get; } = new();
    public ObservableCollection<ToolDetection> VisibleItems { get; } = new();
    public ObservableCollection<string> InstallHistory { get; } = new();

    // ---------- 命令 ----------
    public ICommand DetectCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand UpdateCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand ShowLogCommand { get; }
    public ICommand ShowSystemInfoCommand { get; }
    public ICommand OpenLogFileCommand { get; }
    public ICommand ExportEnvironmentCommand { get; }
    public ICommand ImportEnvironmentCommand { get; }
    public ICommand ScanProjectCommand { get; }

    // ---------- 筛选选项 ----------
    public ObservableCollection<FilterOption> FilterOptions { get; }
    public ObservableCollection<string> StatusFilterOptions { get; } = new() { "全部状态", "已安装", "未安装", "有更新" };

    private string _statusFilter = "全部状态";
    public string StatusFilter
    {
        get => _statusFilter;
        set { _statusFilter = value; OnPropertyChanged(); RefreshFilter(); }
    }

    private FilterOption _selectedFilter;
    public FilterOption SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            _selectedFilter = value;
            FilterMode = value.Mode;
            OnPropertyChanged();
        }
    }

    // ---------- 分类 / 搜索 / 筛选 ----------
    private CategoryNode _selectedCategoryNode;
    public CategoryNode SelectedCategoryNode
    {
        get => _selectedCategoryNode;
        set { _selectedCategoryNode = value; OnPropertyChanged(); RefreshFilter(); }
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); RefreshFilter(); }
    }

    private FilterMode _filterMode = FilterMode.All;
    public FilterMode FilterMode
    {
        get => _filterMode;
        set { _filterMode = value; OnPropertyChanged(); RefreshFilter(); }
    }

    // ---------- 状态 ----------
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    private bool _isInstalling;
    public bool IsInstalling
    {
        get => _isInstalling;
        set { _isInstalling = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    private string _statusText = "就绪";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private int _selectedCount;
    public int SelectedCount
    {
        get => _selectedCount;
        set { _selectedCount = value; OnPropertyChanged(); }
    }

    // ---------- 安装进度 ----------
    private string _currentTool = "";
    public string CurrentTool
    {
        get => _currentTool;
        set { _currentTool = value; OnPropertyChanged(); }
    }

    private string _currentStage = "";
    public string CurrentStage
    {
        get => _currentStage;
        set { _currentStage = value; OnPropertyChanged(); }
    }

    private string _progressDetail = "";
    public string ProgressDetail
    {
        get => _progressDetail;
        set { _progressDetail = value; OnPropertyChanged(); }
    }

    private double _progressValue;
    public double ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; OnPropertyChanged(); }
    }

    private bool _progressIsIndeterminate = true;
    public bool ProgressIsIndeterminate
    {
        get => _progressIsIndeterminate;
        set { _progressIsIndeterminate = value; OnPropertyChanged(); }
    }

    private static readonly System.Text.RegularExpressions.Regex PercentRegex =
        new(@"(\d{1,3})%", System.Text.RegularExpressions.RegexOptions.Compiled);

    private void ParseProgress(string output)
    {
        var m = PercentRegex.Match(output);
        if (m.Success && double.TryParse(m.Groups[1].Value, out var pct) && pct is >= 0 and <= 100)
        {
            ProgressValue = pct;
            ProgressIsIndeterminate = false;
        }
    }

    private bool _showProgress;
    public bool ShowProgress
    {
        get => _showProgress;
        set { _showProgress = value; OnPropertyChanged(); }
    }

    // ---------- 检测 ----------
    public async Task DetectAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "正在检测本机环境...";
        foreach (var d in AllDetections)
        {
            d.Status = DetectionStatus.Detecting;
            d.Version = null;
            d.InstallPath = null;
            d.Message = null;
        }

        // 1) 先获取 winget 已装包列表（用于兜底检测）
        var wingetIds = await Task.Run(() =>
        {
            try { return WingetHelper.ListInstalledIds(); }
            catch { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        });

        // 2) 全量 8 路并发检测，每个软件完成后立即更新 UI
        var engine = new DetectionEngine { WingetInstalledIds = wingetIds };
        var semaphore = new SemaphoreSlim(8);
        var allTools = _toolIndex.Values.ToList();
        int done = 0;

        var tasks = allTools.Select(async tool =>
        {
            await semaphore.WaitAsync();
            try
            {
                var result = await Task.Run(() => engine.DetectSingle(tool));
                await _dispatcher.InvokeAsync(() =>
                {
                    var existing = AllDetections.FirstOrDefault(d =>
                        d.Name.Equals(result.Name, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null)
                    {
                        existing.Status = result.Status;
                        existing.Version = result.Version;
                        existing.InstallPath = result.InstallPath;
                        existing.Message = result.Message;
                    }
                    done++;
                    StatusText = $"正在检测... {done}/{allTools.Count}";
                });
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);

        IsBusy = false;
        int installed = AllDetections.Count(d => d.IsInstalled);
        StatusText = $"检测完成，{installed} 项已安装 / {AllDetections.Count - installed} 项未安装（正在检查更新...）";

        // 3) winget 可更新查询放后台，不阻塞 UI
        _ = Task.Run(async () =>
        {
            HashSet<string> upgradableIds;
            try { upgradableIds = WingetHelper.GetUpgradable().Ids; }
            catch { upgradableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }

            await _dispatcher.InvokeAsync(() =>
            {
                int updated = 0;
                foreach (var d in AllDetections)
                {
                    var firstMethod = d.Tool.Install?.EffectiveMethods.FirstOrDefault();
                    if (d.Status == DetectionStatus.Installed
                        && firstMethod?.MethodEnum == InstallMethod.Winget
                        && !string.IsNullOrEmpty(firstMethod?.Id)
                        && upgradableIds.Contains(firstMethod.Id!))
                    {
                        d.Status = DetectionStatus.UpdateAvailable;
                        updated++;
                    }
                }
                StatusText = $"检测完成，{AllDetections.Count(d => d.IsInstalled)} 项已安装（其中 {updated} 项有更新）/ {AllDetections.Count(d => !d.IsInstalled)} 项未安装";
            });
        });
    }

    // ---------- 安装 ----------
    public async Task InstallAsync()
    {
        if (IsBusy || IsInstalling) return;

        var selected = AllDetections.Where(d => d.IsSelected && !d.IsInstalled).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("请先勾选需要安装的软件。\n（已安装的软件无需再次安装，未自动勾选）", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 依赖分析
        var plan = DependencyResolver.BuildPlan(
            selected.Select(d => d.Tool),
            _toolIndex,
            name => GetDetection(name)?.IsInstalled ?? false);
        if (plan.Count == 0)
        {
            MessageBox.Show("所选项目均已安装，无需安装。", "提示");
            return;
        }

        // 安装前确认（安全要求：安装前显示安装计划）
        var confirm = new InstallPlanWindow(plan) { Owner = Application.Current.MainWindow };
        if (confirm.ShowDialog() != true) return;

        _cts = new CancellationTokenSource();
        IsInstalling = true;
        ShowProgress = true;
        InstallHistory.Clear();
        StatusText = "正在安装...";

        try
        {
            var detIndex = AllDetections.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
            var token = _cts.Token;

            await Task.Run(() =>
            {
                var engine = new InstallEngine(
                    p => _dispatcher.Invoke(() => ApplyProgress(p)),
                    _toolIndex.Values.ToList(),
                    detIndex);
                engine.Execute(plan, token);
            });

            // 安装完成：刷新 PATH + 全量重新检测
            await Task.Run(() => PathManager.RefreshCurrentProcessPath());
            StatusText = "安装流程结束，正在重新检测...";
            await DetectAsync();
            StatusText = "安装流程已结束。";
        }
        catch (OperationCanceledException)
        {
            StatusText = "安装已取消。";
        }
        catch (Exception ex)
        {
            Logger.Error($"安装流程异常: {ex}");
            MessageBox.Show($"安装流程发生异常：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsInstalling = false;
            ShowProgress = false;
            CurrentTool = "";
            CurrentStage = "";
            ProgressDetail = "";
            _cts?.Dispose();
        }
    }

    public void CancelInstall()
    {
        _cts?.Cancel();
        StatusText = "正在取消...";
    }

    // ---------- 更新 ----------
    public async Task UpdateAsync()
    {
        if (IsBusy || IsInstalling) return;
        if (!WingetHelper.IsAvailable())
        {
            MessageBox.Show("winget 不可用，无法检查更新。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        StatusText = "正在更新软件...";
        CurrentTool = "更新";
        CurrentStage = "准备中";
        ShowProgress = true;
        InstallHistory.Clear();

        // 只更新用户选中的、状态为"有更新"的软件
        var toUpdate = AllDetections
            .Where(d => d.IsSelected && d.Status == DetectionStatus.UpdateAvailable)
            .ToList();

        Logger.Info($"更新：用户选中的可更新软件 {toUpdate.Count} 项");

        if (toUpdate.Count == 0)
        {
            IsBusy = false;
            ShowProgress = false;
            CurrentTool = "";
            CurrentStage = "";
            StatusText = "选中的软件均为最新版本";
            return;
        }

        // 3) 逐个升级（只升级选中的软件，不碰系统其他 winget 包）
        _cts = new CancellationTokenSource();
        IsInstalling = true;
        StatusText = "正在更新软件...";
        var token = _cts.Token;
        int success = 0, failed = 0;

        try
        {
            for (int i = 0; i < toUpdate.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var d = toUpdate[i];
                var id = d.Tool.Install!.Id!;
                CurrentTool = d.Name;
                CurrentStage = $"更新中（{i + 1}/{toUpdate.Count}）";
                ProgressDetail = $"正在升级 {d.Name}（{id}）...";
                ProgressValue = 0;
                ProgressIsIndeterminate = true;
                InstallHistory.Add($"▶ 开始更新：{d.Name}");

                int exitCode = await Task.Run(() =>
                    WingetHelper.Upgrade(id,
                        outputCallback: output => _dispatcher.Invoke(() =>
                        {
                            ProgressDetail = output;
                            ParseProgress(output);
                        }),
                        downloadCallback: snap => _dispatcher.Invoke(() =>
                        {
                            if (snap.BytesReceived > 0)
                            {
                                ProgressDetail = $"下载中：已下载 {snap.SizeText}（{snap.SpeedText}）";
                                ProgressIsIndeterminate = true;
                            }
                        }),
                        cancel: token));

                if (exitCode == 0)
                {
                    success++;
                    InstallHistory.Add($"✅ {d.Name}：更新完成");
                }
                else
                {
                    failed++;
                    InstallHistory.Add($"❌ {d.Name}：更新失败（退出码 {exitCode}）");
                }
            }

            await Task.Run(() => PathManager.RefreshCurrentProcessPath());
            StatusText = $"更新完成（成功 {success} / 失败 {failed}），正在重新检测...";
            await DetectAsync();
            StatusText = $"更新流程已结束（成功 {success} / 失败 {failed}）。";
        }
        catch (OperationCanceledException)
        {
            StatusText = "更新已取消。";
        }
        catch (Exception ex)
        {
            Logger.Error($"更新异常: {ex}");
            MessageBox.Show($"更新发生异常：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsInstalling = false;
            IsBusy = false;
            ShowProgress = false;
            CurrentTool = "";
            CurrentStage = "";
            ProgressDetail = "";
            _cts?.Dispose();
        }
    }

    private void ApplyProgress(InstallProgress p)
    {
        CurrentTool = p.ToolName;
        CurrentStage = p.Stage;
        ProgressDetail = p.Detail.Length > 300 ? p.Detail[..300] : p.Detail;
        if (p.Stage is "开始")
        {
            ProgressValue = 0;
            ProgressIsIndeterminate = true;
            InstallHistory.Add($"▶ 开始安装：{p.ToolName}（{p.Detail}）");
        }
        else if (p.Stage is "完成" or "失败" or "已取消")
        {
            ProgressValue = 0;
            ProgressIsIndeterminate = true;
            InstallHistory.Add($"{(p.Stage == "完成" ? "✅" : p.Stage == "失败" ? "❌" : "⏹")} {p.ToolName}：{p.Detail}");
        }
        else if (p.Stage == "下载中")
        {
            // 下载阶段：winget 重定向输出不发进度条，靠文件大小监控
            // ProgressDetail 已由 InstallEngine 格式化为 "已下载 XX MB（YY MB/s）"
            ProgressIsIndeterminate = true;
        }
        else
        {
            ParseProgress(p.Detail);
        }
    }

    // ---------- 筛选 ----------
    private void RefreshFilter()
    {
        VisibleItems.Clear();
        foreach (var d in AllDetections)
        {
            if (!MatchCategory(d)) continue;
            if (!string.IsNullOrWhiteSpace(SearchText) &&
                !d.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) &&
                !d.Description.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            switch (FilterMode)
            {
                case FilterMode.Common when d.Subcategory != ToolSubcategory.Common: continue;
                case FilterMode.Uncommon when d.Subcategory != ToolSubcategory.Uncommon: continue;
            }
            switch (StatusFilter)
            {
                case "已安装" when d.Status != DetectionStatus.Installed: continue;
                case "未安装" when d.Status != DetectionStatus.NotInstalled: continue;
                case "有更新" when d.Status != DetectionStatus.UpdateAvailable: continue;
            }

            VisibleItems.Add(d);
        }
    }

    private bool MatchCategory(ToolDetection d)
    {
        var node = SelectedCategoryNode;
        if (node.Category is not null && d.Category != node.Category) return false;
        if (node.Subcategory is not null && d.Subcategory != node.Subcategory) return false;
        return true;
    }

    private ToolDetection? GetDetection(string name)
        => AllDetections.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void OnDetectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolDetection.IsSelected))
            UpdateSelectedCount();
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = AllDetections.Count(d => d.IsSelected);
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearSelection()
    {
        foreach (var d in AllDetections) d.IsSelected = false;
        UpdateSelectedCount();
    }

    // ---------- 辅助窗口 ----------
    private void ShowLogWindow()
    {
        var w = new LogWindow { Owner = Application.Current.MainWindow };
        w.Show();
    }

    private void ShowSystemInfoWindow()
    {
        var w = new SystemInfoWindow { Owner = Application.Current.MainWindow };
        w.Show();
    }

    private void OpenLogFile()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Logger.LogFile)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开日志文件：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------- 环境导出/导入 ----------
    private void ExportEnvironment()
    {
        var installed = AllDetections.Where(d => d.IsInstalled).ToList();
        if (installed.Count == 0)
        {
            MessageBox.Show("当前没有已安装的开发环境可导出。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"devkit-env-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            Filter = "DevKit 环境文件 (*.json)|*.json",
            Title = "导出开发环境"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            EnvironmentManager.Export(AllDetections, dlg.FileName);
            StatusText = $"已导出 {installed.Count} 个开发环境到 {Path.GetFileName(dlg.FileName)}";
            MessageBox.Show($"成功导出 {installed.Count} 个开发环境配置。\n\n文件：{dlg.FileName}",
                "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Logger.Error($"导出环境失败: {ex}");
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportEnvironment()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "DevKit 环境文件 (*.json)|*.json",
            Title = "导入开发环境"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var detIndex = AllDetections.ToDictionary(d => d.Name, d => d);
            var toInstall = EnvironmentManager.Import(dlg.FileName, detIndex);

            if (toInstall.Count == 0)
            {
                MessageBox.Show("环境文件中的软件都已安装，无需操作。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ClearSelection();
            int selected = 0;
            foreach (var name in toInstall)
            {
                var d = GetDetection(name);
                if (d != null && !d.IsInstalled)
                {
                    d.IsSelected = true;
                    selected++;
                }
            }
            UpdateSelectedCount();
            StatusText = $"已从环境文件勾选 {selected} 个待安装软件，点击「安装」开始还原";
            MessageBox.Show($"环境文件包含 {toInstall.Count} 个软件，已自动勾选其中 {selected} 个未安装项。\n\n点击底部「安装」按钮即可一键还原开发环境。",
                "导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Logger.Error($"导入环境失败: {ex}");
            MessageBox.Show($"导入失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------- 项目依赖检测 ----------
    private void ScanProject()
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择项目根目录，DevKit 将自动检测所需开发环境",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        StatusText = $"正在扫描项目：{dlg.SelectedPath} ...";
        _ = Task.Run(() =>
        {
            var result = ProjectDetector.Detect(dlg.SelectedPath);
            _dispatcher.Invoke(() =>
            {
                if (result.RequiredTools.Count == 0)
                {
                    StatusText = "未识别到已知项目类型";
                    MessageBox.Show("未在该目录中识别到已知的项目类型。\n\n支持：Node.js、Python、Go、Java、Rust、Flutter、PHP、Ruby、C/C++、.NET、Android、Docker、Lua、Haskell、Swift、Zig 等。",
                        "未识别项目", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                ClearSelection();
                int selected = 0;
                var alreadyInstalled = new List<string>();
                foreach (var toolName in result.RequiredTools)
                {
                    var d = GetDetection(toolName);
                    if (d == null) continue;
                    if (d.IsInstalled)
                    {
                        alreadyInstalled.Add(toolName);
                    }
                    else
                    {
                        d.IsSelected = true;
                        selected++;
                    }
                }
                UpdateSelectedCount();

                var typeText = string.Join("、", result.ProjectTypes);
                var toolsText = string.Join("、", result.RequiredTools);
                StatusText = $"检测到 {typeText}，已勾选 {selected} 个待安装项";

                var msg = $"检测到项目类型：{typeText}\n\n" +
                          $"所需开发环境：{toolsText}\n\n" +
                          $"已安装：{(alreadyInstalled.Count > 0 ? string.Join("、", alreadyInstalled) : "无")}\n" +
                          $"待安装：{selected} 个（已自动勾选）\n\n" +
                          $"点击底部「安装」按钮即可一键安装所需环境。";
                MessageBox.Show(msg, "项目检测结果", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
