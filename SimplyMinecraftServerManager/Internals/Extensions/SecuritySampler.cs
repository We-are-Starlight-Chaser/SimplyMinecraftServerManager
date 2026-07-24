// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 安全采样器：提供高性能的反射/序列化调用检测。
/// 使用采样策略：RELEASE 模式下每 128 次调用检测一次（~0.8% 采样率）。
/// </summary>
internal static class SecuritySampler
{
    // 采样计数器（每扩展独立，使用 ConcurrentDictionary 避免锁竞争）
    private static readonly ConcurrentDictionary<string, int> _callCounters = new();

    // 采样间隔（位运算优化：128 = 2^7）
    private const int SampleMask = 127; // 等价于 % 128，但更快

    /// <summary>
    /// 递增并获取计数器值。
    /// </summary>
    private static int IncrementCounter(string extensionId)
    {
        return _callCounters.AddOrUpdate(extensionId, 1, (_, current) => current + 1);
    }

    /// <summary>
    /// 检测反射调用是否安全。
    /// 使用采样策略，高性能低开销。
    /// </summary>
    /// <param name="extensionId">扩展ID</param>
    /// <param name="reflectionGuard">反射守卫</param>
    /// <param name="method">要检查的方法</param>
    /// <param name="memberName">成员名称（可选）</param>
    /// <returns>true 表示应阻止</returns>
    public static bool ShouldBlockReflection(
        string extensionId,
        ReflectionGuard? reflectionGuard,
        MethodBase? method,
        string? memberName = null)
    {
        if (reflectionGuard is null) return false;

#if DEBUG
        // DEBUG 模式：每次都检测
        return reflectionGuard.IsReflectionCallBlocked(method, memberName);
#else
        // RELEASE 模式：采样检测（每 128 次检测一次）
        int count = IncrementCounter(extensionId);
        if ((count & SampleMask) == 0)
        {
            return reflectionGuard.IsReflectionCallBlocked(method, memberName);
        }
        return false;
#endif
    }

    /// <summary>
    /// 检测序列化调用是否安全。
    /// 使用采样策略，高性能低开销。
    /// </summary>
    /// <param name="extensionId">扩展ID</param>
    /// <param name="serializationGuard">序列化守卫</param>
    /// <param name="serializationType">序列化类型</param>
    /// <param name="methodName">方法名称（可选）</param>
    /// <returns>true 表示应阻止</returns>
    public static bool ShouldBlockSerialization(
        string extensionId,
        SerializationGuard? serializationGuard,
        Type? serializationType,
        string? methodName = null)
    {
        if (serializationGuard is null) return false;

#if DEBUG
        // DEBUG 模式：每次都检测
        return serializationGuard.IsSerializationCallBlocked(serializationType, methodName);
#else
        // RELEASE 模式：采样检测（每 128 次检测一次）
        int count = IncrementCounter(extensionId);
        if ((count & SampleMask) == 0)
        {
            return serializationGuard.IsSerializationCallBlocked(serializationType, methodName);
        }
        return false;
#endif
    }

    /// <summary>
    /// 检测序列化路径是否安全。
    /// </summary>
    public static bool ShouldBlockSerializationPath(
        string extensionId,
        SerializationGuard? serializationGuard,
        string filePath)
    {
        if (serializationGuard is null) return false;

#if DEBUG
        return serializationGuard.IsSerializationPathBlocked(filePath);
#else
        int count = IncrementCounter(extensionId);
        if ((count & SampleMask) == 0)
        {
            return serializationGuard.IsSerializationPathBlocked(filePath);
        }
        return false;
#endif
    }

    /// <summary>
    /// 检测序列化数据是否安全。
    /// </summary>
    public static bool IsSerializedDataSafe(
        string extensionId,
        SerializationGuard? serializationGuard,
        byte[] data)
    {
        if (serializationGuard is null) return true;

#if DEBUG
        return serializationGuard.IsSerializedDataSafe(data);
#else
        int count = IncrementCounter(extensionId);
        if ((count & SampleMask) == 0)
        {
            return serializationGuard.IsSerializedDataSafe(data);
        }
        return true;
#endif
    }

    /// <summary>
    /// 清理指定扩展的计数器。
    /// </summary>
    public static void Cleanup(string extensionId)
    {
        _callCounters.TryRemove(extensionId, out _);
    }

    /// <summary>
    /// 清理所有计数器。
    /// </summary>
    public static void CleanupAll()
    {
        _callCounters.Clear();
    }
}
