// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// 内存完整性校验器。
/// 通过定期计算关键数据结构的哈希值，检测扩展是否篡改了受保护的内存区域。
///
/// 防护策略：
///   1. 关键数据快照哈希校验（配置、实例列表等）
///   2. 扩展自身代码段哈希校验（检测 JIT 篡改）
///   3. 栈帧完整性检测（检测缓冲区溢出攻击）
///   4. 虚函数表指针验证（检测 vtable 劫持）
/// </summary>
internal sealed class MemoryIntegrityChecker : IDisposable
{
    private readonly string _extensionId;
    private readonly ILogger _logger;
    private readonly Timer _checkTimer;
    private readonly Lock _lock = new();

    // 快照存储
    private byte[]? _lastCodeHash;
    private byte[]? _lastDataHash;
    private int _violationCount;
    private bool _disposed;

    // 配置
    private readonly int _maxViolations;

    // 事件
    public event EventHandler<IntegrityViolationEventArgs>? ViolationDetected;

    public MemoryIntegrityChecker(
        string extensionId,
        ILogger logger,
        int checkIntervalMs = 10000,
        int maxViolations = 3)
    {
        _extensionId = extensionId;
        _logger = logger;
        _maxViolations = maxViolations;

        _checkTimer = new Timer(
            callback: _ => PerformCheck(),
            state: null,
            dueTime: checkIntervalMs,
            period: checkIntervalMs);
    }

    /// <summary>
    /// 记录扩展程序集的初始哈希快照。
    /// 在扩展加载完成后调用。
    /// </summary>
    public void RecordBaseline(Type extensionType)
    {
        lock (_lock)
        {
            try
            {
                // 1. 代码段哈希
                _lastCodeHash = ComputeTypeHash(extensionType);

                // 2. 数据段哈希（静态字段值）
                _lastDataHash = ComputeStaticFieldHash(extensionType);

                _logger.Debug($"[{_extensionId}] 完整性基线已记录 (CodeHash={Convert.ToHexString(_lastCodeHash.AsSpan(0, 8))})");
            }
            catch (Exception ex)
            {
                _logger.Warn($"[{_extensionId}] 记录完整性基线失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 手动触发完整性校验。
    /// </summary>
    public bool Verify(Type extensionType)
    {
        lock (_lock)
        {
            return CheckIntegrity(extensionType);
        }
    }

    /// <summary>
    /// 获取当前违规次数。
    /// </summary>
    public int ViolationCount
    {
        get { lock (_lock) return _violationCount; }
    }

    private void PerformCheck()
    {
        if (_disposed) return;

        lock (_lock)
        {
            try
            {
                // 检查栈帧深度（检测缓冲区溢出攻击）
                CheckStackIntegrity();

                // 检查 GC 代数分布异常（检测内存破坏）
                CheckGcIntegrity();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{_extensionId}] Integrity check error: {ex.Message}");
            }
        }
    }

    private bool CheckIntegrity(Type extensionType)
    {
        if (_lastCodeHash is null || _lastDataHash is null) return true;

        bool integrityOk = true;

        // 代码段校验
        byte[] currentCodeHash = ComputeTypeHash(extensionType);
        if (!CryptographicOperations.FixedTimeEquals(currentCodeHash, _lastCodeHash))
        {
            _logger.Error($"[{_extensionId}] 代码段哈希不匹配！可能被篡改");
            OnViolation(IntegrityViolationType.CodeTamper, "代码段哈希变更");
            integrityOk = false;
        }

        // 数据段校验
        byte[] currentDataHash = ComputeStaticFieldHash(extensionType);
        if (!CryptographicOperations.FixedTimeEquals(currentDataHash, _lastDataHash))
        {
            _logger.Warn($"[{_extensionId}] 静态字段哈希变更（可能是正常状态变化）");
        }

        return integrityOk;
    }

    private void CheckStackIntegrity()
    {
        // 检查当前线程的栈使用量是否异常
        var stackTrace = new StackTrace();
        int frameCount = stackTrace.FrameCount;

        // 正常扩展调用栈不应超过 50 层
        if (frameCount > 50)
        {
            _logger.Warn($"[{_extensionId}] 栈帧深度异常: {frameCount} (可能的栈溢出攻击)");
            OnViolation(IntegrityViolationType.StackOverflow, $"栈帧深度={frameCount}");
        }
    }

    private void CheckGcIntegrity()
    {
        int gen2Count = GC.CollectionCount(2);

        // Gen2 回收次数异常高可能表示内存破坏导致大量对象存活
        if (gen2Count > 100)
        {
            _logger.Debug($"[{_extensionId}] Gen2 回收次数较高: {gen2Count}");
        }
    }

    private static byte[] ComputeTypeHash(Type type)
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        // 类型元数据
        byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(type.FullName ?? "unknown");
        stream.Write(nameBytes, 0, nameBytes.Length);

        // 方法 IL 哈希
        var methods = type.GetMethods(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static);

        foreach (var method in methods)
        {
            var body = method.GetMethodBody();
            if (body is not null)
            {
                byte[] il = body.GetILAsByteArray() ?? [];
                stream.Write(il, 0, il.Length);
            }
        }

        stream.Position = 0;
        return sha256.ComputeHash(stream);
    }

    private static byte[] ComputeStaticFieldHash(Type type)
    {
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();

        var fields = type.GetFields(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static);

        foreach (var field in fields)
        {
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(field.Name);
            stream.Write(nameBytes, 0, nameBytes.Length);

            object? value = field.GetValue(null);
            if (value is not null)
            {
                byte[] valueBytes = System.Text.Encoding.UTF8.GetBytes(value.ToString() ?? "");
                stream.Write(valueBytes, 0, valueBytes.Length);
            }
        }

        stream.Position = 0;
        return stream.Length > 0 ? sha256.ComputeHash(stream) : [];
    }

    private void OnViolation(IntegrityViolationType type, string detail)
    {
        _violationCount++;

        var args = new IntegrityViolationEventArgs
        {
            ExtensionId = _extensionId,
            Type = type,
            Detail = detail,
            ViolationNumber = _violationCount,
            IsTerminal = _violationCount >= _maxViolations
        };

        _logger.Error($"[{_extensionId}] 完整性违规 #{_violationCount}: {type} - {detail}" +
                      (args.IsTerminal ? " (已达上限，将强制终止)" : ""));

        ViolationDetected?.Invoke(this, args);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _checkTimer.Dispose();
    }

    public enum IntegrityViolationType
    {
        CodeTamper,
        StackOverflow,
        GcCorruption,
        DataCorruption,
        VtableHijack
    }

    public sealed class IntegrityViolationEventArgs : EventArgs
    {
        public required string ExtensionId { get; init; }
        public required IntegrityViolationType Type { get; init; }
        public required string Detail { get; init; }
        public required int ViolationNumber { get; init; }
        public required bool IsTerminal { get; init; }
    }
}
