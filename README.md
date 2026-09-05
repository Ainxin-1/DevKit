# DevKit — 开发环境工具箱

**Windows Developer Environment Toolkit**

自动检测 Windows 电脑中的**开发环境**与**包管理器**，按「开发环境 / 包管理器 × ⭐常用 / 🧰不常用」分类展示，
支持勾选、依赖分析、批量安装（多方式自动回退：winget → scoop → 官方命令）、实时进度、日志与安装后自动复检。
内置应用商店，可动态搜索 winget 上万个软件包。

核心流程：**检测 → 分类 → 选择 → 检查依赖 → 安装 → 再检测**

---

## 一、功能特性

- **自动检测**：检测已安装的开发环境 / 包管理器，显示状态、版本号、安装路径
  - 状态：已安装 / 未安装 / 版本过低 / 有更新 / 检测中 / 检测失败（彩色徽章）
  - 检测方式：PATH 命令 + 版本命令 + 环境变量（JAVA_HOME / ANDROID_HOME）+ 常见安装目录 + winget 已装列表
  - 8 路并行检测，逐个完成立即渲染
- **分类展示**：一级分类（开发环境 / 包管理器），二级分类（⭐ 常用 / 🧰 不常用）
- **自动检测更新**：启动时自动查询 winget upgrade，有更新的软件显示橙色"有更新"徽章
- **搜索与筛选**：顶部搜索框；筛选 [全部][常用][不常用]；状态筛选 [全部状态][已安装][未安装][有更新]
- **依赖分析**：自动展开依赖（如 pip → Python、pnpm → Node.js、Maven → JDK、pub → Flutter），
  已安装的依赖不重复安装，按依赖优先排序生成安装计划
- **批量安装**：可一次勾选多项，安装前展示计划确认，实时进度 + 历史记录 + 取消支持
- **多方式自动回退**：每个软件支持多种安装方式，按优先级尝试，失败自动换下一个
  - `winget`：安装前验证包 ID，不存在时搜索候选
  - `scoop`：需要 scoop 已安装，未安装时自动先装 scoop
  - `official`：官方命令（如 pip 的 ensurepip）
  - `bundled`：随宿主安装（npm 随 Node.js、Cargo 随 Rust）
- **一键更新**：选中有更新的软件，点击"更新"直接升级（无确认弹窗）
- **应用商店**：动态搜索 winget 上万个软件包，支持安装、更新、卸载
- **实时进度条**：从 winget 输出解析下载百分比，真实进度而非装饰
- **日志**：所有检测与安装命令写入日志（`%LOCALAPPDATA%\DevKit\logs\`），可查看日志 / 打开日志文件
- **系统信息**：Windows 版本、架构、CPU、内存、磁盘、管理员权限、PowerShell、winget
- **JSON 配置驱动**：新增软件只需编辑 `config/tools.json`，无需修改代码

## 二、项目目录

```
DevKit/
├── DevKit.sln              # 解决方案
├── README.md                      # 本文档
└── DevKit/
    ├── DevKit.csproj       # 项目文件（net8.0-windows / WPF）
    ├── App.xaml / App.xaml.cs     # 应用入口
    ├── MainWindow.xaml(.cs)       # 主窗口（左分类 + 右列表 + 搜索筛选 + 安装进度）
    ├── config/
    │   └── tools.json             # ★ 软件清单配置（可自行扩展）
    ├── Models/
    │   ├── Enums.cs               # 分类 / 状态 / 安装方式枚举
    │   ├── ToolInfo.cs            # 软件配置模型
    │   └── ToolDetection.cs       # 检测结果模型（INPC）
    ├── Core/
    │   ├── ConfigLoader.cs        # 加载 tools.json
    │   ├── CommandRunner.cs       # 命令执行器（超时/取消/输出回调/日志）
    │   ├── WingetHelper.cs        # winget 封装（search/show/install/list）
    │   ├── DetectionEngine.cs     # 检测引擎（PATH/版本/路径/组件）
    │   ├── DependencyResolver.cs  # 依赖分析器（生成安装计划）
    │   ├── InstallEngine.cs       # 安装引擎（计划执行/进度/复检）
    │   ├── PathManager.cs         # 当前进程 PATH 刷新（不写注册表）
    │   ├── SystemInfoProvider.cs  # 系统环境信息
    │   └── Logger.cs              # 日志（文件 + 内存）
    ├── ViewModels/
    │   ├── MainViewModel.cs       # 主界面 VM（MVVM）
    │   └── RelayCommand.cs
    └── Views/
        ├── InstallPlanWindow.xaml # 安装计划确认窗口
        ├── LogWindow.xaml         # 日志查看窗口
        └── SystemInfoWindow.xaml  # 系统信息窗口
```

## 三、开发环境要求

| 依赖 | 版本 | 说明 |
|---|---|---|
| .NET SDK | 8.0+ | 编译与运行（含 Windows Desktop 工作负载，SDK 自带） |
| winget | 1.4+ | 安装软件的核心通道（Win10 1809+ 一般自带） |
| Windows | 10 / 11 | 目标平台 |

安装 .NET SDK（官方源）：

```powershell
winget install --id Microsoft.DotNet.SDK.8 -e --silent --accept-package-agreements --accept-source-agreements
```

## 四、编译方法

```powershell
cd DevKit
dotnet restore
dotnet build DevKit.sln -c Debug
```

开发运行：

```powershell
dotnet run --project DevKit
```

## 五、Windows EXE 打包方法

**方式 A：依赖框架单文件（体积小，需目标机有 .NET 8 运行时）**

```powershell
dotnet publish DevKit/DevKit.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

**方式 B：自包含单文件（免装运行时，体积约 100MB+）**

```powershell
dotnet publish DevKit/DevKit.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish-standalone
```

打包产物：`publish\DevKit.exe` + `publish\config\tools.json`（整个 publish 目录一起分发）。

## 六、测试方法

### 自动验证
```powershell
# 1) 编译必须 0 错误 0 警告
dotnet build DevKit.sln -c Release

# 2) 启动程序，进程应稳定存活，日志生成于 %LOCALAPPDATA%\DevKit\logs\
```

### 手动测试清单（对照需求）

| # | 用例 | 预期 |
|---|---|---|
| 1 | 启动软件 | 自动检测，列表显示各软件状态/版本/路径 |
| 2 | 切换左侧 4 个分类 | 右侧只显示对应分类的软件 |
| 3 | 搜索框输入 "python" | 显示 Python + pip/Poetry/Pipenv/Conda 等 |
| 4 | 筛选 [未安装] / ☑只显示未安装 | 只显示未安装项 |
| 5 | 勾选 pnpm（未装 Node） | 安装计划出现 Node.js(依赖) + pnpm |
| 6 | 勾选 pip（Python 已装） | 安装计划只出现 pip，不重复装 Python |
| 7 | 点击 [安装选中] | 弹出计划确认 → 确认后开始安装，进度实时显示 |
| 8 | 安装完成后 | 该项状态自动复检刷新，列表更新 |
| 9 | 点击 [查看日志] | 日志窗口显示检测/安装记录 |
| 10 | 点击 [系统信息] | 显示系统概览 |
| 11 | 编辑 tools.json 增加软件 | 重启后新软件出现在列表 |

## 七、使用说明

1. 双击 `DevKit.exe` 启动（首次启动自动检测，约数秒）
2. 左侧选择分类（开发环境 / 包管理器 × 常用 / 不常用），顶部可搜索、筛选（全部 / 仅未安装）
3. 右侧勾选需要安装的软件（**已安装的不会自动勾选**）；**点击整行任意位置即可切换勾选**，勾选后复选框显示蓝色 √
4. 点击「⬇ 安装选中」→ 查看安装计划（含自动补充的依赖）→ 确认
5. 等待安装完成（底部进度卡片显示当前工具、阶段与历史记录，可随时取消），列表自动复检刷新
6. 需要时通过「查看日志」「系统信息」了解详情

> 界面说明：采用卡片式现代布局，状态以彩色徽章展示（已安装/未安装/版本过低/检测失败），安装方式、依赖、结果一目了然。

## 八、配置扩展（config/tools.json）

每个软件一个 JSON 节点，字段说明：

```json
{
  "name": "Swift",
  "category": "environment",
  "subcategory": "common",
  "description": "Swift 语言工具链",
  "detect": {
    "command": "swift",
    "versionArgs": "--version",
    "versionRegex": "(\\d+\\.\\d+(\\.\\d+)?)",
    "pathHints": ["C:\\Library\\Developer\\Toolchains"]
  },
  "dependencies": [],
  "install": {
    "methods": [
      { "method": "winget", "id": "Swift.Toolchain" },
      { "method": "scoop", "id": "swift", "requires": "Scoop" }
    ]
  }
}
```

安装方式说明：
- `winget`：通过 winget 安装，`id` 为包 ID
- `scoop`：通过 scoop 安装，`requires` 指定前置依赖（如 Scoop），未安装时自动先装
- `official`：执行官方命令，`officialCommand` 指定命令
- `bundled`：随宿主软件自带，安装依赖后自动获得

## 九、安全设计（对照需求文档）

1. 不执行隐藏的危险命令——所有命令明文展示并写入日志
2. 安装前显示安装计划，用户确认后才执行
3. 优先使用 winget 官方源（`--source` 默认官方仓库），禁止来源不明的 EXE
4. winget 包 ID 安装前用 `winget show` 验证，不盲目信任配置中的 ID
5. 不关闭 Windows Defender、不修改无关系统设置
6. 不偷偷修改 PATH——只刷新当前进程环境变量（注册表 PATH 不做写入）
7. 管理员权限仅在需要时提示（如 Chocolatey）
8. 不保存密码、不收集隐私；日志仅记录本地操作

## 十、已知限制

- 框架依赖版本需要目标机安装 .NET 8 桌面运行时
- 部分软件的版本过低判定依赖 `minVersion` 配置，可自行调整
- 安装后立即复检依赖当前进程 PATH 刷新；新开的终端窗口才能完整使用新工具
- 应用商店搜索依赖 winget search 的文本解析，极端情况下可能解析异常
