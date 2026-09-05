using System.IO;
using DevKit.Models;

namespace DevKit.Core;

/// <summary>
/// 项目依赖检测：扫描项目目录，识别项目类型和所需运行时，
/// 自动推荐需要安装的开发环境和包管理器。
/// 借鉴 DevBox 自动识别项目依赖的思路。
/// </summary>
public static class ProjectDetector
{
    /// <summary>检测结果</summary>
    public class ProjectDetectResult
    {
        public string ProjectPath { get; set; } = "";
        public List<string> ProjectTypes { get; set; } = new();
        public List<string> RequiredTools { get; set; } = new();
        public List<string> FoundFiles { get; set; } = new();
    }

    /// <summary>项目文件 -> 所需工具的映射规则</summary>
    private static readonly (string FilePattern, string[] ProjectTypes, string[] RequiredTools)[] Rules = new[]
    {
        // Node.js / 前端
        ("package.json", new[] { "Node.js 项目" }, new[] { "Node.js", "npm" }),
        ("pnpm-lock.yaml", new[] { "pnpm 项目" }, new[] { "Node.js", "pnpm" }),
        ("yarn.lock", new[] { "Yarn 项目" }, new[] { "Node.js", "Yarn" }),
        ("bun.lockb", new[] { "Bun 项目" }, new[] { "Bun" }),
        ("bun.lock", new[] { "Bun 项目" }, new[] { "Bun" }),
        ("deno.json", new[] { "Deno 项目" }, new[] { "Node.js" }),

        // Python
        ("requirements.txt", new[] { "Python 项目" }, new[] { "Python", "pip" }),
        ("pyproject.toml", new[] { "Python 项目" }, new[] { "Python", "pip" }),
        ("setup.py", new[] { "Python 项目" }, new[] { "Python", "pip" }),
        ("Pipfile", new[] { "Pipenv 项目" }, new[] { "Python", "Pipenv" }),
        ("poetry.lock", new[] { "Poetry 项目" }, new[] { "Python", "Poetry" }),
        ("pdm.lock", new[] { "PDM 项目" }, new[] { "Python", "PDM" }),
        ("uv.lock", new[] { "uv 项目" }, new[] { "Python", "uv" }),
        ("environment.yml", new[] { "Conda 项目" }, new[] { "Python", "Conda" }),
        ("environment.yaml", new[] { "Conda 项目" }, new[] { "Python", "Conda" }),

        // Go
        ("go.mod", new[] { "Go 项目" }, new[] { "Go" }),
        ("go.sum", new[] { "Go 项目" }, new[] { "Go" }),

        // Java / JVM
        ("pom.xml", new[] { "Maven 项目" }, new[] { "JDK", "Maven" }),
        ("build.gradle", new[] { "Gradle 项目" }, new[] { "JDK", "Gradle" }),
        ("build.gradle.kts", new[] { "Gradle 项目" }, new[] { "JDK", "Gradle" }),
        ("settings.gradle", new[] { "Gradle 项目" }, new[] { "JDK", "Gradle" }),

        // Rust
        ("Cargo.toml", new[] { "Rust 项目" }, new[] { "Rust", "Cargo" }),
        ("Cargo.lock", new[] { "Rust 项目" }, new[] { "Rust", "Cargo" }),

        // Flutter / Dart
        ("pubspec.yaml", new[] { "Flutter/Dart 项目" }, new[] { "Flutter" }),

        // PHP
        ("composer.json", new[] { "PHP 项目" }, new[] { "PHP", "Composer" }),
        ("composer.lock", new[] { "PHP 项目" }, new[] { "PHP", "Composer" }),

        // Ruby
        ("Gemfile", new[] { "Ruby 项目" }, new[] { "Ruby", "Bundler" }),
        ("Gemfile.lock", new[] { "Ruby 项目" }, new[] { "Ruby", "Bundler" }),

        // C/C++
        ("CMakeLists.txt", new[] { "CMake 项目" }, new[] { "CMake", "MinGW" }),
        ("Makefile", new[] { "C/C++ 项目" }, new[] { "MinGW" }),
        ("configure.ac", new[] { "C/C++ 项目" }, new[] { "MinGW" }),
        ("vcpkg.json", new[] { "vcpkg 项目" }, new[] { "vcpkg", "CMake" }),
        ("conanfile.txt", new[] { "Conan 项目" }, new[] { "Conan", "CMake" }),
        ("conanfile.py", new[] { "Conan 项目" }, new[] { "Conan", "CMake" }),

        // .NET
        ("*.sln", new[] { ".NET 解决方案" }, new[] { ".NET SDK" }),
        ("*.csproj", new[] { ".NET 项目" }, new[] { ".NET SDK" }),
        ("*.fsproj", new[] { "F# 项目" }, new[] { ".NET SDK" }),

        // Android
        ("AndroidManifest.xml", new[] { "Android 项目" }, new[] { "Android SDK", "JDK" }),
        ("build.gradle", new[] { "Android/Gradle 项目" }, new[] { "Android SDK", "JDK" }),

        // Docker
        ("Dockerfile", new[] { "Docker 项目" }, new[] { "Docker Desktop" }),
        ("docker-compose.yml", new[] { "Docker Compose 项目" }, new[] { "Docker Desktop" }),
        ("docker-compose.yaml", new[] { "Docker Compose 项目" }, new[] { "Docker Desktop" }),

        // Lua
        ("*.rockspec", new[] { "Lua 项目" }, new[] { "Lua", "LuaRocks" }),

        // Haskell
        ("*.cabal", new[] { "Haskell 项目" }, new[] { "Haskell", "Cabal" }),
        ("stack.yaml", new[] { "Haskell Stack 项目" }, new[] { "Haskell" }),

        // Swift
        ("Package.swift", new[] { "Swift 项目" }, new[] { "Swift" }),

        // Zig
        ("build.zig", new[] { "Zig 项目" }, new[] { "MinGW" }),

        // Git
        (".git", new[] { "Git 仓库" }, new[] { "Git" }),
    };

    /// <summary>扫描项目目录，检测所需开发环境</summary>
    public static ProjectDetectResult Detect(string projectPath, int maxDepth = 3)
    {
        var result = new ProjectDetectResult { ProjectPath = projectPath };
        if (!Directory.Exists(projectPath)) return result;

        var foundFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectTypes = new HashSet<string>();
        var requiredTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 扫描目录（限制深度，避免太慢）
        ScanDirectory(projectPath, foundFiles, maxDepth, 0);

        // 匹配规则
        foreach (var (pattern, types, tools) in Rules)
        {
            bool matched = false;
            if (pattern.Contains('*'))
            {
                matched = foundFiles.Any(f => Path.GetFileName(f).Like(pattern));
            }
            else
            {
                matched = foundFiles.Any(f => Path.GetFileName(f).Equals(pattern, StringComparison.OrdinalIgnoreCase));
            }

            if (matched)
            {
                foreach (var t in types) projectTypes.Add(t);
                foreach (var t in tools) requiredTools.Add(t);
            }
        }

        result.ProjectTypes = projectTypes.ToList();
        result.RequiredTools = requiredTools.ToList();
        result.FoundFiles = foundFiles.Take(30).ToList();

        Logger.Info($"项目检测：{projectPath} -> 类型[{string.Join(",", projectTypes)}] -> 需要[{string.Join(",", requiredTools)}]");
        return result;
    }

    private static void ScanDirectory(string dir, HashSet<string> files, int maxDepth, int currentDepth)
    {
        if (currentDepth > maxDepth) return;
        try
        {
            // 只检查当前目录的文件（不递归太深）
            foreach (var file in Directory.GetFiles(dir))
            {
                files.Add(file);
            }
            // 检查子目录名（如 .git）
            foreach (var subDir in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(subDir);
                files.Add(name); // 目录名也参与匹配（如 .git）
                // 跳过 node_modules、.git 等大目录
                if (name is "node_modules" or ".git" or "bin" or "obj" or "dist" or "build" or "target" or "vendor")
                    continue;
                if (currentDepth < maxDepth)
                    ScanDirectory(subDir, files, maxDepth, currentDepth + 1);
            }
        }
        catch { /* 忽略无权限目录 */ }
    }

    /// <summary>简单通配符匹配（* 匹配任意字符）</summary>
    private static bool Like(this string input, string pattern)
    {
        if (pattern == "*") return true;
        if (pattern.StartsWith('*') && pattern.EndsWith('*'))
            return input.Contains(pattern.Trim('*'), StringComparison.OrdinalIgnoreCase);
        if (pattern.StartsWith('*'))
            return input.EndsWith(pattern.TrimStart('*'), StringComparison.OrdinalIgnoreCase);
        if (pattern.EndsWith('*'))
            return input.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase);
        return input.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
