using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using DevKit.Core;

namespace DevKit.ViewModels;

/// <summary>
/// 应用商店页 ViewModel：动态搜索 winget 源，安装/更新/卸载任意软件。
/// </summary>
public class StoreViewModel : INotifyPropertyChanged
{
    private string _searchText = "";
    private bool _isSearching;
    private bool _isBusy;
    private string _statusText = "输入关键词搜索 winget 软件源";
    private HashSet<string> _installedIds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _upgradableIds = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<StoreItem> Results { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); }
    }

    public bool IsSearching
    {
        get => _isSearching;
        set { _isSearching = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); }
    }

    public bool IsNotBusy => !IsBusy && !IsSearching;

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public ICommand SearchCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand UpdateCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand RefreshStatusCommand { get; }

    public StoreViewModel()
    {
        SearchCommand = new RelayCommand(_ => _ = SearchAsync(), _ => IsNotBusy && !string.IsNullOrWhiteSpace(SearchText));
        InstallCommand = new RelayCommand(obj => _ = InstallAsync((StoreItem)obj!), _ => IsNotBusy);
        UpdateCommand = new RelayCommand(obj => _ = UpdateAsync((StoreItem)obj!), _ => IsNotBusy);
        UninstallCommand = new RelayCommand(obj => _ = UninstallAsync((StoreItem)obj!), _ => IsNotBusy);
        RefreshStatusCommand = new RelayCommand(_ => _ = RefreshStatusAsync());
    }

    /// <summary>搜索 winget 源</summary>
    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText) || IsSearching) return;
        IsSearching = true;
        StatusText = $"正在搜索 \"{SearchText.Trim()}\" ...";
        Results.Clear();

        try
        {
            // 后台线程只执行 winget search，不碰 UI
            List<WingetHelper.PackageInfo> list = null!;
            await Task.Run(() => { list = WingetHelper.Search(SearchText.Trim(), max: 50); });

            // await 后回到 UI 线程，批量添加
            foreach (var p in list)
            {
                Results.Add(new StoreItem
                {
                    Name = p.Name,
                    Id = p.Id,
                    Version = p.Version,
                    Source = p.Source,
                    IsInstalled = _installedIds.Contains(p.Id),
                    HasUpdate = _upgradableIds.Contains(p.Id)
                });
            }
            StatusText = $"找到 {Results.Count} 个结果";
        }
        catch (Exception ex)
        {
            StatusText = $"搜索失败: {ex.Message}";
            Logger.Error($"应用商店搜索失败: {ex}");
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>刷新已安装/可更新状态（搜索前后调用）</summary>
    public async Task RefreshStatusAsync()
    {
        IsBusy = true;
        StatusText = "正在刷新安装状态...";
        try
        {
            await Task.Run(() =>
            {
                _installedIds = WingetHelper.ListInstalledIds();
                _upgradableIds = WingetHelper.GetUpgradable().Ids;
            });
            foreach (var item in Results)
            {
                item.IsInstalled = _installedIds.Contains(item.Id);
                item.HasUpdate = _upgradableIds.Contains(item.Id);
            }
            StatusText = $"已安装 {_installedIds.Count} 个 winget 包，{_upgradableIds.Count} 个可更新";
        }
        catch (Exception ex)
        {
            StatusText = $"刷新状态失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallAsync(StoreItem item)
    {
        if (item.IsBusy) return;
        item.IsBusy = true;
        IsBusy = true;
        StatusText = $"正在安装 {item.Name} ...";
        try
        {
            int exit = await Task.Run(() => WingetHelper.Install(item.Id, line => StatusText = line));
            if (exit == 0)
            {
                item.IsInstalled = true;
                item.HasUpdate = false;
                StatusText = $"{item.Name} 安装成功";
                _installedIds.Add(item.Id);
            }
            else
            {
                StatusText = $"{item.Name} 安装失败（退出码 {exit}），查看日志";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"安装异常: {ex.Message}";
            Logger.Error($"应用商店安装失败 {item.Id}: {ex}");
        }
        finally
        {
            item.IsBusy = false;
            IsBusy = false;
        }
    }

    private async Task UpdateAsync(StoreItem item)
    {
        if (item.IsBusy) return;
        item.IsBusy = true;
        IsBusy = true;
        StatusText = $"正在更新 {item.Name} ...";
        try
        {
            int exit = await Task.Run(() => WingetHelper.Upgrade(item.Id, line => StatusText = line));
            if (exit == 0)
            {
                item.HasUpdate = false;
                StatusText = $"{item.Name} 更新成功";
                _upgradableIds.Remove(item.Id);
            }
            else
            {
                StatusText = $"{item.Name} 更新失败（退出码 {exit}）";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"更新异常: {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
            IsBusy = false;
        }
    }

    private async Task UninstallAsync(StoreItem item)
    {
        if (item.IsBusy) return;
        item.IsBusy = true;
        IsBusy = true;
        StatusText = $"正在卸载 {item.Name} ...";
        try
        {
            int exit = await Task.Run(() => WingetHelper.Uninstall(item.Id, line => StatusText = line));
            if (exit == 0)
            {
                item.IsInstalled = false;
                item.HasUpdate = false;
                StatusText = $"{item.Name} 卸载成功";
                _installedIds.Remove(item.Id);
            }
            else
            {
                StatusText = $"{item.Name} 卸载失败（退出码 {exit}）";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"卸载异常: {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
            IsBusy = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>应用商店中的单个软件项</summary>
public class StoreItem : INotifyPropertyChanged
{
    private bool _isInstalled;
    private bool _hasUpdate;
    private bool _isBusy;

    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string Source { get; set; } = "";

    public bool IsInstalled
    {
        get => _isInstalled;
        set { _isInstalled = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusColor)); }
    }

    public bool HasUpdate
    {
        get => _hasUpdate;
        set { _hasUpdate = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusColor)); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public string StatusText => HasUpdate ? "可更新" : IsInstalled ? "已安装" : "未安装";
    public string StatusColor => HasUpdate ? "#E65100" : IsInstalled ? "#2E7D32" : "#5B6573";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
