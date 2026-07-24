// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 安全服务接口，供扩展主动查询自身安全状态和执行安全校验。
/// 所有方法均为只读查询，不会修改安全策略。
/// </summary>
public interface ISecurityService
{
    /// <summary>当前扩展的网络违规次数</summary>
    int NetworkViolationCount { get; }

    /// <summary>当前扩展的内存使用量（字节）</summary>
    long MemoryUsageBytes { get; }

    /// <summary>当前扩展持有的句柄数</summary>
    int HandleCount { get; }

    /// <summary>扩展是否已被安全策略强制终止</summary>
    bool IsTerminated { get; }

    /// <summary>
    /// 验证出站网络请求是否允许。
    /// </summary>
    bool ValidateOutboundRequest(string method, string url, string? contentType = null);

    /// <summary>
    /// 验证 URL 是否安全（防 SSRF / DNS 重绑定）。
    /// </summary>
    Task<bool> ValidateUrlAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// 验证进程创建是否允许。
    /// </summary>
    bool ValidateProcessCreation(string fileName, string? arguments = null);

    /// <summary>
    /// 验证 P/Invoke 调用是否允许。
    /// </summary>
    bool ValidatePInvokeCall(string libraryName, string functionName);

    /// <summary>
    /// 检查反射调用是否被阻止。
    /// </summary>
    bool IsReflectionCallBlocked(System.Reflection.MethodBase? method, string? memberName = null);

    /// <summary>
    /// 检查序列化调用是否被阻止。
    /// </summary>
    bool IsSerializationCallBlocked(Type? serializationType, string? methodName = null);

    /// <summary>
    /// 获取当前扩展的安全状态摘要。
    /// </summary>
    SecurityStatus GetStatus();
}

/// <summary>
/// 扩展安全状态摘要。
/// </summary>
public sealed class SecurityStatus
{
    /// <summary>网络违规次数</summary>
    public int NetworkViolationCount { get; init; }

    /// <summary>内存使用量（字节）</summary>
    public long MemoryUsageBytes { get; init; }

    /// <summary>峰值内存使用量（字节）</summary>
    public long PeakMemoryUsageBytes { get; init; }

    /// <summary>当前句柄数</summary>
    public int HandleCount { get; init; }

    /// <summary>是否已被终止</summary>
    public bool IsTerminated { get; init; }

    /// <summary>文件操作次数</summary>
    public long FileOperationCount { get; init; }

    /// <summary>网络请求次数</summary>
    public int NetworkRequestCount { get; init; }
}
