using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// Guards against reflection attacks by restricting access to sensitive reflection methods.
/// Extensions should use the provided SDK interfaces instead of reflection.
/// </summary>
internal sealed class ReflectionGuard : IDisposable
{
    private readonly string _extensionId;
    private readonly ExtensionLogger? _logger;
    private readonly int _maxReflectionCallsPerSecond;
    private int _reflectionCallCount;
    private long _lastWindowStartTicks;
    private bool _disposed;
    
    // Dangerous reflection methods that should be restricted
    private static readonly HashSet<string> DangerousReflectionMethods = new()
    {
        // Type access methods
        "GetType",
        "typeof",
        
        // Member access methods
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
        
        // Value manipulation methods
        "GetValue",
        "SetValue",
        
        // Binding flags manipulation
        "GetCustomAttribute",
        "GetCustomAttributes",
        
        // Assembly loading
        "Load",
        "LoadFrom",
        "LoadFile",
        "ReflectionOnlyLoad",
        "ReflectionOnlyLoadFrom",
        
        // Type creation
        "CreateInstance",
        "Activator.CreateInstance",
        
        // Delegate creation
        "CreateDelegate",
        "CreateComCallableWrapper",
        
        // Security critical methods
        "GetTypeInfo",
        "MakeGenericType",
        "MakeArrayType",
        "MakePointerType",
        "GetElementType",
        "GetGenericArguments",
        "GetGenericParameterConstraints",
        "GetGenericTypeDefinition",
        
        // Module access
        "GetModule",
        "GetModules",
        "GetLoadedModules",
        "GetExportedTypes",
        "GetTypes",
    };
    
    // Dangerous types that should be blocked from reflection
    private static readonly HashSet<string> DangerousTypes = new()
    {
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
    };
    
    // Dangerous flags combinations that indicate suspicious reflection usage
    private static readonly BindingFlags DangerousFlags = BindingFlags.NonPublic | BindingFlags.Static;
    
    public ReflectionGuard(string extensionId, int maxReflectionCallsPerSecond = 100, ExtensionLogger? logger = null)
    {
        _extensionId = extensionId;
        _maxReflectionCallsPerSecond = maxReflectionCallsPerSecond;
        _logger = logger;
        _reflectionCallCount = 0;
        _lastWindowStartTicks = DateTime.UtcNow.Ticks;
    }
    
    /// <summary>
    /// Checks if a reflection call is allowed based on the target member and context.
    /// Returns true if the call should be blocked.
    /// </summary>
    public bool IsReflectionCallBlocked(MethodBase? method, string? memberName = null)
    {
        if (_disposed)
            return false;
        
        // Check reflection call rate limit
        if (!CheckReflectionCallRate())
        {
            _logger?.Warn($"Reflection rate limit exceeded for extension {_extensionId}: {_maxReflectionCallsPerSecond} calls/sec");
            return true;
        }
        
        if (method == null)
            return false;
        
        var declaringType = method.DeclaringType;
        var methodName = method.Name;
        
        // Check if accessing dangerous types
        if (declaringType != null)
        {
            var typeName = declaringType.FullName ?? declaringType.Name;
            
            if (IsDangerousType(typeName))
            {
                _logger?.Warn($"Blocked reflection access to dangerous type {typeName} in extension {_extensionId}");
                return true;
            }
        }
        
        // Check if method is dangerous
        if (IsDangerousMethod(methodName))
        {
            _logger?.Warn($"Blocked reflection access to dangerous method {methodName} in extension {_extensionId}");
            return true;
        }
        
        // Check for suspicious flag combinations (NonPublic + Static)
        if (method is MethodInfo methodInfo)
        {
            var flags = methodInfo.Attributes;
            if ((flags & MethodAttributes.Private) != 0 && (flags & MethodAttributes.Static) != 0)
            {
                _logger?.Warn($"Blocked reflection access to private static method {methodName} in extension {_extensionId}");
                return true;
            }
            
            if ((flags & MethodAttributes.FamANDAssem) != 0 && (flags & MethodAttributes.Static) != 0)
            {
                _logger?.Warn($"Blocked reflection access to family-and-assembly static method {methodName} in extension {_extensionId}");
                return true;
            }
        }
        
        // Check for field/property access with dangerous flags
        if (memberName != null)
        {
            if (IsDangerousMemberAccess(memberName, method))
            {
                _logger?.Warn($"Blocked reflection access to dangerous member {memberName} in extension {_extensionId}");
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Checks if a type is in the dangerous list.
    /// </summary>
    private bool IsDangerousType(string typeName)
    {
        return DangerousTypes.Any(dangerous =>
            typeName.Contains(dangerous, StringComparison.OrdinalIgnoreCase) ||
            dangerous.Contains(typeName, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Checks if a method name is in the dangerous list.
    /// </summary>
    private bool IsDangerousMethod(string methodName)
    {
        return DangerousReflectionMethods.Contains(methodName, StringComparer.OrdinalIgnoreCase);
    }
    
    /// <summary>
    /// Checks for suspicious member access patterns.
    /// </summary>
    private bool IsDangerousMemberAccess(string memberName, MethodBase method)
    {
        // Check for access to private/internal members
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
        
        // Check for access to static members via reflection
        // Note: MethodBase cannot be directly cast to FieldInfo/PropertyInfo
        // This check is simplified for now
        
        return false;
    }
    
    /// <summary>
    /// Checks reflection call rate limit.
    /// </summary>
    private bool CheckReflectionCallRate()
    {
        var now = DateTime.UtcNow.Ticks;
        var elapsed = now - Interlocked.Read(ref _lastWindowStartTicks);
        var elapsedMs = elapsed / TimeSpan.TicksPerMillisecond;
        
        if (elapsedMs >= 1000) // 1 second window
        {
            Interlocked.Exchange(ref _reflectionCallCount, 0);
            Interlocked.Exchange(ref _lastWindowStartTicks, now);
            return true;
        }
        
        var currentCount = Interlocked.Increment(ref _reflectionCallCount);
        return currentCount <= _maxReflectionCallsPerSecond;
    }
    
    /// <summary>
    /// Gets the count of reflection calls in the current window.
    /// </summary>
    public int GetReflectionCallCount()
    {
        return _reflectionCallCount;
    }
    
    /// <summary>
    /// Resets the reflection call counter.
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
