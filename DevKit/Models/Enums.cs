namespace DevKit.Models;

/// <summary>一级分类：开发环境 / 包管理器</summary>
public enum ToolCategory
{
    Environment,
    PackageManager
}

/// <summary>二级分类：常用 / 不常用</summary>
public enum ToolSubcategory
{
    Common,
    Uncommon
}

/// <summary>检测状态</summary>
public enum DetectionStatus
{
    /// <summary>检测中</summary>
    Detecting,
    /// <summary>已安装（最新）</summary>
    Installed,
    /// <summary>未安装</summary>
    NotInstalled,
    /// <summary>已安装但版本过低</summary>
    VersionTooLow,
    /// <summary>检测失败</summary>
    DetectFailed,
    /// <summary>已安装但有可用更新</summary>
    UpdateAvailable
}

/// <summary>安装方式</summary>
public enum InstallMethod
{
    /// <summary>通过 winget 安装（安装前会查询确认包 ID）</summary>
    Winget,
    /// <summary>通过 scoop 安装（需要 scoop 已安装）</summary>
    Scoop,
    /// <summary>随宿主软件自带（如 npm 随 Node.js 安装）</summary>
    Bundled,
    /// <summary>官方脚本/命令（如 pip 的 ensurepip）</summary>
    Official
}
