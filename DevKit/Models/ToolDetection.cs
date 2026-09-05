using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DevKit.Models;

/// <summary>
/// 一个软件在本次扫描中的检测结果。
/// </summary>
public class ToolDetection : INotifyPropertyChanged
{
    public required ToolInfo Tool { get; init; }

    private DetectionStatus _status = DetectionStatus.Detecting;
    public DetectionStatus Status
    {
        get => _status;
        set { if (_status != value) { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(IsInstalled)); } }
    }

    private string? _version;
    public string? Version
    {
        get => _version;
        set { if (_version != value) { _version = value; OnPropertyChanged(); OnPropertyChanged(nameof(VersionText)); } }
    }

    private string? _installPath;
    public string? InstallPath
    {
        get => _installPath;
        set { if (_installPath != value) { _installPath = value; OnPropertyChanged(); } }
    }

    private string? _message;
    public string? Message
    {
        get => _message;
        set { if (_message != value) { _message = value; OnPropertyChanged(); } }
    }

    private string? _installResult;
    public string? InstallResult
    {
        get => _installResult;
        set { if (_installResult != value) { _installResult = value; OnPropertyChanged(); } }
    }

    private InstallResultStatus _installStatus = InstallResultStatus.None;
    public InstallResultStatus InstallStatus
    {
        get => _installStatus;
        set { if (_installStatus != value) { _installStatus = value; OnPropertyChanged(); } }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public string Name => Tool.Name;
    public string Description => Tool.Description;
    public ToolCategory Category => Tool.CategoryEnum;
    public ToolSubcategory Subcategory => Tool.SubcategoryEnum;
    public bool IsInstalled => Status is DetectionStatus.Installed or DetectionStatus.VersionTooLow or DetectionStatus.UpdateAvailable or DetectionStatus.VersionUnknown;

    /// <summary>用于 UI 的状态文本</summary>
    public string StatusText => Status switch
    {
        DetectionStatus.Installed => "已安装",
        DetectionStatus.NotInstalled => "未安装",
        DetectionStatus.VersionTooLow => "版本过低",
        DetectionStatus.UpdateAvailable => "有更新",
        DetectionStatus.VersionUnknown => "版本未知",
        DetectionStatus.Detecting => "检测中",
        _ => "检测失败"
    };

    /// <summary>UI 显示用版本（未安装显示 "-"）</summary>
    public string VersionText => string.IsNullOrEmpty(Version) ? "-" : Version;

    public string InstallMethodText => Tool.Install?.EffectiveMethods.FirstOrDefault()?.MethodEnum switch
    {
        InstallMethod.Winget => "winget",
        InstallMethod.Scoop => "scoop",
        InstallMethod.Bundled => "随宿主安装",
        InstallMethod.Official => "官方命令",
        _ => "-"
    };

    public string DependencyText => Tool.Dependencies.Count == 0
        ? "-"
        : string.Join(", ", Tool.Dependencies);

    public string PathText => string.IsNullOrEmpty(InstallPath) ? "-" : InstallPath;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
