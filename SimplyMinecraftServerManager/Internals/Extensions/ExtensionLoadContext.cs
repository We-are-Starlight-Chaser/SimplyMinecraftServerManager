// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 扩展专用的 AssemblyLoadContext，提供程序集隔离。
/// 每个扩展加载在独立的上下文中，卸载时可回收所有程序集。
/// 宿主核心程序集始终从 Default 上下文加载，避免类型重复。
/// </summary>
internal sealed class ExtensionLoadContext(string extensionId) : AssemblyLoadContext(isCollectible: true)
{
    private readonly string _extensionPath = Path.GetFullPath(Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "extensions", extensionId));

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null) return null;

        // 宿主核心程序集始终从 Default 上下文加载，避免类型重复
        if (assemblyName.Name.StartsWith("SimplyMinecraftServerManager", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string assemblyPath = Path.Combine(_extensionPath, $"{assemblyName.Name}.dll");
        if (File.Exists(assemblyPath))
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        // 未找到则返回 null，回退到默认上下文
        return null;
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
}
