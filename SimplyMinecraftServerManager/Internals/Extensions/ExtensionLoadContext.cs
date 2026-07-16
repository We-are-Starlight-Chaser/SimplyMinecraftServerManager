// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 扩展专用的 AssemblyLoadContext，提供程序集隔离。
/// 每个扩展加载在独立的上下文中，卸载时可回收所有程序集。
///
/// SDK 程序集加载策略（强制统一）：
///   1. 所有以 "SimplyMinecraftServerManager" 开头的程序集 → 从 Default 上下文加载（主程序提供的 SDK DLL）
///   2. 加载前检测扩展目录中是否存在私自携带的 SDK DLL → 拒绝并警告
///   3. 其他第三方程序集 → 从扩展目录加载
/// </summary>
internal sealed class ExtensionLoadContext(string extensionId) : AssemblyLoadContext(isCollectible: true)
{
    private readonly string _extensionPath = Path.GetFullPath(Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "extensions", extensionId));

    private readonly ILogger _logger = new ExtensionLogger(extensionId);
    
    // Track loaded assemblies to detect suspicious loading patterns
    private readonly HashSet<string> _loadedAssemblies = new();
    private readonly object _lock = new();
    
    // Maximum number of assemblies an extension can load
    private const int MaxAssemblyLoadCount = 100;
    
    // Allowed paths for assembly loading
    private static readonly string[] AllowedAssemblyPaths = [];

    /// <summary>
    /// SDK 程序集名称前缀（所有以此开头的程序集必须从主程序加载）。
    /// </summary>
    private static readonly string[] SdkPrefixes =
    [
        "SimplyMinecraftServerManager",
    ];
    
    // Dangerous assembly patterns that should be blocked
    private static readonly string[] DangerousAssemblyPatterns =
    [
        "System.Runtime.Serialization",
        "System.Security",
        "System.Diagnostics.Process",
        "System.Management",
        "System.DirectoryServices",
        "System.Configuration",
        "System.Data.SqlClient",
        "System.Web",
    ];

    /// <summary>
    /// 检查扩展目录中是否携带了 SDK DLL。
    /// 返回所有违规的 SDK DLL 文件路径。 
    /// </summary>
    public IReadOnlyList<string> DetectBundledSdkDlls()
    {
        var bundledDlls = new List<string>();

        if (!Directory.Exists(_extensionPath))
        {
            return bundledDlls;
        }

        string[] allDlls = Directory.GetFiles(_extensionPath, "*.dll", SearchOption.TopDirectoryOnly);

        foreach (string dllPath in allDlls)
        {
            string fileName = Path.GetFileNameWithoutExtension(dllPath);

            if (IsSdkAssembly(fileName))
            {
                bundledDlls.Add(dllPath);
            }
        }

        return bundledDlls;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null) return null;

        // 所有 SDK 程序集必须从 Default 上下文加载（使用主程序提供的 DLL）
        if (IsSdkAssembly(assemblyName.Name))
        {
            // 即使扩展目录中有同名 DLL，也强制从 Default 上下文加载
            // 这确保所有扩展使用同一个 SDK 版本，避免类型不一致
            return null;
        }
        
        // Check if loading dangerous assemblies
        if (IsDangerousAssembly(assemblyName.Name))
        {
            _logger.Warn($"Blocked loading of dangerous assembly {assemblyName.Name} in extension {extensionId}");
            return null;
        }
        
        // Check assembly load count limit
        lock (_lock)
        {
            if (_loadedAssemblies.Count >= MaxAssemblyLoadCount)
            {
                _logger.Warn($"Extension {extensionId} exceeded maximum assembly load count ({MaxAssemblyLoadCount})");
                return null;
            }
            
            _loadedAssemblies.Add(assemblyName.Name);
        }

        string assemblyPath = Path.Combine(_extensionPath, $"{assemblyName.Name}.dll");
        if (File.Exists(assemblyPath))
        {
            // Validate assembly path is within allowed directories
            if (!IsAssemblyPathAllowed(assemblyPath))
            {
                _logger.Warn($"Blocked loading assembly from disallowed path {assemblyPath} in extension {extensionId}");
                return null;
            }
            
            return LoadFromAssemblyPath(assemblyPath);
        }

        // 未找到则返回 null，回退到默认上下文
        return null;
    }
    
    /// <summary>
    /// Checks if the assembly path is within allowed directories.
    /// </summary>
    private bool IsAssemblyPathAllowed(string assemblyPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(assemblyPath);
            
            // Allow loading from extension directory
            if (fullPath.StartsWith(_extensionPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            
            // Allow loading from main application directory
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            if (fullPath.StartsWith(appDir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            
            // Allow loading from .NET shared framework directories
            var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
            if (fullPath.StartsWith(runtimeDir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Checks if the assembly is dangerous and should be blocked.
    /// </summary>
    private static bool IsDangerousAssembly(string assemblyName)
    {
        foreach (var pattern in DangerousAssemblyPatterns)
        {
            if (assemblyName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string unmanagedPath = Path.Combine(_extensionPath, unmanagedDllName);
        if (File.Exists(unmanagedPath))
        {
            return LoadUnmanagedDllFromPath(unmanagedPath);
        }

        return IntPtr.Zero;
    }

    /// <summary>卸载扩展的所有程序集</summary>
    public void UnloadExtension()
    {
        Unload();
    }

    /// <summary>
    /// 判断程序集名称是否属于 SDK。
    /// </summary>
    private static bool IsSdkAssembly(string assemblyName)
    {
        foreach (string prefix in SdkPrefixes)
        {
            if (assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
