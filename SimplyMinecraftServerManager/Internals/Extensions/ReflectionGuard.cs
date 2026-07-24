using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 防护反射攻击，限制对敏感反射方法的访问。
/// 扩展应使用提供的 SDK 接口而非反射。
/// </summary>
internal sealed class ReflectionGuard(string extensionId, int maxReflectionCallsPerSecond = 100, ExtensionLogger? logger = null) : IDisposable
{
    private readonly string _extensionId = extensionId;
    private readonly ExtensionLogger? _logger = logger;
    private readonly int _maxReflectionCallsPerSecond = maxReflectionCallsPerSecond;
    private int _reflectionCallCount = 0;
    private long _lastWindowStartTicks = DateTime.UtcNow.Ticks;
    private bool _disposed;
    
    // 应被限制的危险反射方法
    private static readonly HashSet<string> DangerousReflectionMethods =
    [
        // 类型访问方法
        "GetType",
        "typeof",
        
        // 成员访问方法
        "GetMember",
        "GetMembers",
        "GetField",
        "GetFields",
        "GetProperty",
        "GetProperties",
        "GetMethod",
        "GetMethods",
        "GetConstructor",
        "GetConstructors",
        "GetEvent",
        "GetEvents",
        "GetInterface",
        "GetInterfaces",
        
        // 值操作方法
        "GetValue",
        "SetValue",
        
        // 绑定标志操作
        "GetCustomAttribute",
        "GetCustomAttributes",
        
        // 程序集加载
        "Load",
        "LoadFrom",
        "LoadFile",
        "ReflectionOnlyLoad",
        "ReflectionOnlyLoadFrom",
        
        // 类型创建
        "CreateInstance",
        "Activator.CreateInstance",
        
        // 委托创建
        "CreateDelegate",
        "CreateComCallableWrapper",
        
        // 安全关键方法
        "GetTypeInfo",
        "MakeGenericType",
        "MakeArrayType",
        "MakePointerType",
        "GetElementType",
        "GetGenericArguments",
        "GetGenericParameterConstraints",
        "GetGenericTypeDefinition",
        
        // 模块访问
        "GetModule",
        "GetModules",
        "GetLoadedModules",
        "GetExportedTypes",
        "GetTypes",
    ];
    
    // 应被反射阻止的危险类型
    private static readonly HashSet<string> DangerousTypes =
    [
        "System.Security.SecurityManager",
        "System.Security.PermissionSet",
        "System.Security.Policy.Evidence",
        "System.Security.Policy.CodeGroup",
        "System.Security.Policy.PolicyLevel",
        "System.AppDomain",
        "System.AppDomainSetup",
        "System.AppDomainManager",
        "System.Runtime.Remoting.RemotingConfiguration",
        "System.Runtime.Remoting.Channels.ChannelServices",
        "System.Reflection.Emit.DynamicMethod",
        "System.Reflection.Emit.TypeBuilder",
        "System.Reflection.Emit.ModuleBuilder",
        "System.Reflection.Emit.AssemblyBuilder",
        "System.Runtime.InteropServices.Marshal",
        "System.Runtime.CompilerServices.RuntimeHelpers",
        "System.Environment",
        "System.Environment.SpecialFolder",
        "System.IO.FileInfo",
        "System.IO.DirectoryInfo",
        "System.Diagnostics.Process",
        "System.Diagnostics.ProcessStartInfo",
    ];
    
    // 表示可疑反射使用的危险标志组合
    private static readonly BindingFlags DangerousFlags = BindingFlags.NonPublic | BindingFlags.Static;

    /// <summary>
    /// 根据目标成员和上下文检查反射调用是否被允许。
    /// 如果调用应被阻止则返回 true。
    /// </summary>
    public bool IsReflectionCallBlocked(MethodBase? method, string? memberName = null)
    {
        if (_disposed)
            return false;
        
        // 检查反射调用速率限制
        if (!CheckReflectionCallRate())
        {
            _logger?.Warn($"扩展 {_extensionId} 反射速率超限: {_maxReflectionCallsPerSecond} 次/秒");
            return true;
        }
        
        if (method == null)
            return false;
        
        var declaringType = method.DeclaringType;
        var methodName = method.Name;
        
        // 检查是否访问危险类型
        if (declaringType != null)
        {
            var typeName = declaringType.FullName ?? declaringType.Name;
            
            if (IsDangerousType(typeName))
            {
                _logger?.Warn($"阻止反射访问危险类型 {typeName}，扩展 {_extensionId}");
                return true;
            }
        }
        
        // 检查方法是否危险
        if (IsDangerousMethod(methodName))
        {
            _logger?.Warn($"阻止反射访问危险方法 {methodName}，扩展 {_extensionId}");
            return true;
        }
        
        // 检查可疑的标志组合（NonPublic + Static）
        if (method is MethodInfo methodInfo)
        {
            var flags = methodInfo.Attributes;
            if ((flags & MethodAttributes.Private) != 0 && (flags & MethodAttributes.Static) != 0)
            {
                _logger?.Warn($"阻止反射访问私有静态方法 {methodName}，扩展 {_extensionId}");
                return true;
            }
            
            if ((flags & MethodAttributes.FamANDAssem) != 0 && (flags & MethodAttributes.Static) != 0)
            {
                _logger?.Warn($"阻止反射访问族及程序集静态方法 {methodName}，扩展 {_extensionId}");
                return true;
            }
        }
        
        // 检查使用危险标志的字段/属性访问
        if (memberName != null)
        {
            if (IsDangerousMemberAccess(memberName, method))
            {
                _logger?.Warn($"阻止反射访问危险成员 {memberName}，扩展 {_extensionId}");
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// 检查类型是否在危险列表中（使用 HashSet O(1) 查找替代 O(n) 线性扫描）。
    /// </summary>
    private static bool IsDangerousType(string typeName)
    {
        // 快速精确匹配
        if (DangerousTypes.Contains(typeName))
            return true;

        // 仅在精确匹配失败时进行子串检查（低频路径）
        foreach (string dangerous in DangerousTypes)
        {
            if (typeName.Contains(dangerous, StringComparison.OrdinalIgnoreCase) ||
                dangerous.Contains(typeName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
    
    /// <summary>
    /// 检查方法名是否在危险列表中。
    /// </summary>
    private static bool IsDangerousMethod(string methodName)
    {
        return DangerousReflectionMethods.Contains(methodName, StringComparer.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// 检查可疑的成员访问模式。
    /// </summary>
    private static bool IsDangerousMemberAccess(string memberName, MethodBase method)
    {
        // 检查是否访问私有/内部成员
        if (method is MethodInfo methodInfo)
        {
            var flags = methodInfo.Attributes;
            if ((flags & MethodAttributes.Private) != 0 ||
                (flags & MethodAttributes.FamANDAssem) != 0 ||
                (flags & MethodAttributes.Assembly) != 0)
            {
                return true;
            }
        }
        
        // 检查通过反射访问静态成员
        // 注意：MethodBase 无法直接转换为 FieldInfo/PropertyInfo
        // 此检查目前已简化
        
        return false;
    }
    
    /// <summary>
    /// 检查反射调用速率限制。
    /// </summary>
    private bool CheckReflectionCallRate()
    {
        long now = DateTime.UtcNow.Ticks;
        long lastStart = Interlocked.Read(ref _lastWindowStartTicks);
        long elapsed = now - lastStart;
        var elapsedMs = elapsed / TimeSpan.TicksPerMillisecond;
        
        if (elapsedMs >= 1000) // 1秒时间窗口
        {
            // 使用 CAS 循环原子重置，避免竞态
            long oldStart = Interlocked.CompareExchange(ref _lastWindowStartTicks, now, lastStart);
            if (oldStart == lastStart)
            {
                // 成功重置窗口，重置计数器
                Interlocked.Exchange(ref _reflectionCallCount, 0);
                return true;
            }
            // 其他线程已重置，继续检查计数
        }
        
        var currentCount = Interlocked.Increment(ref _reflectionCallCount);
        return currentCount <= _maxReflectionCallsPerSecond;
    }
    
    /// <summary>
    /// 获取当前窗口内的反射调用次数。
    /// </summary>
    public int GetReflectionCallCount()
    {
        return _reflectionCallCount;
    }
    
    /// <summary>
    /// 重置反射调用计数器。
    /// </summary>
    public void ResetCounters()
    {
        Interlocked.Exchange(ref _reflectionCallCount, 0);
        Interlocked.Exchange(ref _lastWindowStartTicks, DateTime.UtcNow.Ticks);
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
