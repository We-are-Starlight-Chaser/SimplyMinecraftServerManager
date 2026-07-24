// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Reflection;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// ISecurityService 实现，桥接到各安全守卫组件。
/// 所有查询均为只读，不修改安全策略。
/// </summary>
internal sealed class SecurityServiceImpl(
    string extensionId,
    NetworkGuard? networkGuard = null,
    ProcessGuard? processGuard = null,
    PInvokeGuard? pInvokeGuard = null,
    ReflectionGuard? reflectionGuard = null,
    SerializationGuard? serializationGuard = null,
    MemoryGuard? memoryGuard = null,
    HandleMonitor? handleMonitor = null) : ISecurityService
{
    private readonly string _extensionId = extensionId;
    private readonly NetworkGuard? _networkGuard = networkGuard;
    private readonly ProcessGuard? _processGuard = processGuard;
    private readonly PInvokeGuard? _pInvokeGuard = pInvokeGuard;
    private readonly ReflectionGuard? _reflectionGuard = reflectionGuard;
    private readonly SerializationGuard? _serializationGuard = serializationGuard;
    private readonly MemoryGuard? _memoryGuard = memoryGuard;
    private readonly HandleMonitor? _handleMonitor = handleMonitor;

    public int NetworkViolationCount => _networkGuard?.ViolationCount ?? 0;

    public long MemoryUsageBytes => _memoryGuard?.CurrentManagedBytes ?? 0;

    public int HandleCount => (_handleMonitor?.ActiveFileHandleCount ?? 0) + (_handleMonitor?.ActiveProcessHandleCount ?? 0);

    public bool IsTerminated => false;

    public bool ValidateOutboundRequest(string method, string url, string? contentType = null)
    {
        if (_networkGuard is null) return false;
        return _networkGuard.ValidateOutbound(method, url, contentType);
    }

    public async Task<bool> ValidateUrlAsync(string url, CancellationToken ct = default)
    {
        if (_networkGuard is null) return false;
        return await _networkGuard.ValidateUrl(url).ConfigureAwait(false);
    }

    public bool ValidateProcessCreation(string fileName, string? arguments = null)
    {
        if (_processGuard is null) return false;
        return _processGuard.ValidateProcessCreation(fileName, arguments);
    }

    public bool ValidatePInvokeCall(string libraryName, string functionName)
    {
        if (_pInvokeGuard is null) return false;
        return _pInvokeGuard.ValidatePInvokeCall(libraryName, functionName);
    }

    public bool IsReflectionCallBlocked(MethodBase? method, string? memberName = null)
    {
        if (_reflectionGuard is null) return false;
        return _reflectionGuard.IsReflectionCallBlocked(method, memberName);
    }

    public bool IsSerializationCallBlocked(Type? serializationType, string? methodName = null)
    {
        if (_serializationGuard is null) return false;
        return _serializationGuard.IsSerializationCallBlocked(serializationType, methodName);
    }

    public SecurityStatus GetStatus()
    {
        return new SecurityStatus
        {
            NetworkViolationCount = NetworkViolationCount,
            MemoryUsageBytes = MemoryUsageBytes,
            PeakMemoryUsageBytes = _memoryGuard?.PeakManagedBytes ?? 0,
            HandleCount = HandleCount,
            IsTerminated = IsTerminated,
            NetworkRequestCount = _networkGuard?.ConcurrentRequests ?? 0,
        };
    }
}
