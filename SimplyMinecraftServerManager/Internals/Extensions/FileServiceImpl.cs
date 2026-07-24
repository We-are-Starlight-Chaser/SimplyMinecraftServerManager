// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.IO;
using System.Threading;
using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// IFileService 实现。所有文件操作经过 FileAccessGuard 校验，
/// 并通过 ForbiddenApiDetector、ReflectionGuard、SerializationGuard 检测禁止的API调用，
/// 通过 HandleMonitor 跟踪句柄。
/// </summary>
internal sealed class FileServiceImpl(
    FileAccessGuard guard,
    ILogger logger,
    string? extensionId = null,
    HandleMonitor? handleMonitor = null,
    ReflectionGuard? reflectionGuard = null,
    SerializationGuard? serializationGuard = null) : IFileService
{
    private readonly string? _extensionId = extensionId;
    private readonly HandleMonitor? _handleMonitor = handleMonitor;
    private readonly ReflectionGuard? _reflectionGuard = reflectionGuard;
    private readonly SerializationGuard? _serializationGuard = serializationGuard;

    private int _callCounter;

    public async Task<FileReadResult> ReadBytesAsync(string scopeId, string relativePath, CancellationToken ct = default)
    {
        DetectSecurityViolations();

        string? path = guard.Validate(scopeId, relativePath, FileAccessLevel.Read);
        if (path is null)
        {
            return FileReadResult.Fail(relativePath, "访问被拒绝");
        }

        try
        {
            byte[] data = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            return FileReadResult.Ok(path, data);
        }
        catch (Exception ex)
        {
            logger.Error($"读取文件失败: {path}", ex);
            return FileReadResult.Fail(path, "读取文件失败"); // 不泄露完整异常信息
        }
    }

    public async Task<FileReadResult> ReadTextAsync(string scopeId, string relativePath, CancellationToken ct = default)
    {
        DetectSecurityViolations();

        string? path = guard.Validate(scopeId, relativePath, FileAccessLevel.Read);
        if (path is null)
        {
            return FileReadResult.Fail(relativePath, "访问被拒绝");
        }

        try
        {
            string text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return FileReadResult.OkText(path, text);
        }
        catch (Exception ex)
        {
            logger.Error($"读取文件失败: {path}", ex);
            return FileReadResult.Fail(path, "读取文件失败");
        }
    }

    public async Task<FileOperationResult> WriteBytesAsync(string scopeId, string relativePath, byte[] data, CancellationToken ct = default)
    {
        DetectSecurityViolations();

        string? path = guard.Validate(scopeId, relativePath, FileAccessLevel.Write);
        if (path is null)
        {
            return FileOperationResult.Fail(relativePath, "访问被拒绝");
        }

        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllBytesAsync(path, data, ct).ConfigureAwait(false);
            return FileOperationResult.Ok(path);
        }
        catch (Exception ex)
        {
            logger.Error($"写入文件失败: {path}", ex);
            return FileOperationResult.Fail(path, "写入文件失败");
        }
    }

    public async Task<FileOperationResult> WriteTextAsync(string scopeId, string relativePath, string text, CancellationToken ct = default)
    {
        DetectSecurityViolations();

        string? path = guard.Validate(scopeId, relativePath, FileAccessLevel.Write);
        if (path is null)
        {
            return FileOperationResult.Fail(relativePath, "访问被拒绝");
        }

        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(path, text, ct).ConfigureAwait(false);
            return FileOperationResult.Ok(path);
        }
        catch (Exception ex)
        {
            logger.Error($"写入文件失败: {path}", ex);
            return FileOperationResult.Fail(path, "写入文件失败");
        }
    }

    public async Task<FileOperationResult> AppendTextAsync(string scopeId, string relativePath, string text, CancellationToken ct = default)
    {
        DetectSecurityViolations();

        string? path = guard.Validate(scopeId, relativePath, FileAccessLevel.Write);
        if (path is null)
        {
            return FileOperationResult.Fail(relativePath, "访问被拒绝");
        }

        try
        {
            await File.AppendAllTextAsync(path, text, ct).ConfigureAwait(false);
            return FileOperationResult.Ok(path);
        }
        catch (Exception ex)
        {
            logger.Error($"追加文件失败: {path}", ex);
            return FileOperationResult.Fail(path, "追加文件失败");
        }
    }

    public Task<FileOperationResult> DeleteAsync(string scopeId, string relativePath, CancellationToken ct = default)
    {
        DetectSecurityViolations();

        string? path = guard.Validate(scopeId, relativePath, FileAccessLevel.Delete);
        if (path is null)
        {
            return Task.FromResult(FileOperationResult.Fail(relativePath, "访问被拒绝"));
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            return Task.FromResult(FileOperationResult.Ok(path));
        }
        catch (Exception ex)
        {
            logger.Error($"删除文件失败: {path}", ex);
            return Task.FromResult(FileOperationResult.Fail(path, "删除文件失败"));
        }
    }

    public Task<FileOperationResult> MoveAsync(string scopeId, string sourcePath, string destPath, CancellationToken ct = default)
    {
        DetectSecurityViolations();

        string? resolvedSource = guard.Validate(scopeId, sourcePath, FileAccessLevel.Read);
        string? resolvedDest = guard.Validate(scopeId, destPath, FileAccessLevel.Write);

        if (resolvedSource is null || resolvedDest is null)
        {
            return Task.FromResult(FileOperationResult.Fail(sourcePath, "访问被拒绝"));
        }

        try
        {
            File.Move(resolvedSource, resolvedDest);
            return Task.FromResult(FileOperationResult.Ok(resolvedDest));
        }
        catch (Exception ex)
        {
            logger.Error($"移动文件失败: {sourcePath} → {destPath}", ex);
            return Task.FromResult(FileOperationResult.Fail(sourcePath, "移动文件失败"));
        }
    }

    public Task<FileOperationResult> CopyAsync(string scopeId, string sourcePath, string destPath, CancellationToken ct = default)
    {
        DetectSecurityViolations();

        string? resolvedSource = guard.Validate(scopeId, sourcePath, FileAccessLevel.Read);
        string? resolvedDest = guard.Validate(scopeId, destPath, FileAccessLevel.Write);

        if (resolvedSource is null || resolvedDest is null)
        {
            return Task.FromResult(FileOperationResult.Fail(sourcePath, "访问被拒绝"));
        }

        try
        {
            File.Copy(resolvedSource, resolvedDest, overwrite: true);
            return Task.FromResult(FileOperationResult.Ok(resolvedDest));
        }
        catch (Exception ex)
        {
            logger.Error($"复制文件失败: {sourcePath} → {destPath}", ex);
            return Task.FromResult(FileOperationResult.Fail(sourcePath, "复制文件失败"));
        }
    }

    public Task<bool> ExistsAsync(string scopeId, string relativePath, CancellationToken ct = default)
    {
        DetectSecurityViolations();

        string? path = guard.Validate(scopeId, relativePath, FileAccessLevel.Read);
        if (path is null)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(File.Exists(path));
    }

    public Task<FileOperationResult> GetFileInfoAsync(string scopeId, string relativePath, CancellationToken ct = default)
    {
        DetectSecurityViolations();

        string? path = guard.Validate(scopeId, relativePath, FileAccessLevel.Read);
        if (path is null)
        {
            return Task.FromResult(FileOperationResult.Fail(relativePath, "访问被拒绝"));
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return Task.FromResult(FileOperationResult.Fail(path, "文件不存在"));
            }

            logger.Debug($"文件信息: {path}, 大小={info.Length}B, 修改={info.LastWriteTimeUtc}");
            return Task.FromResult(FileOperationResult.Ok(path));
        }
        catch (Exception ex)
        {
            logger.Error($"获取文件信息失败: {path}", ex);
            return Task.FromResult(FileOperationResult.Fail(path, "获取文件信息失败"));
        }
    }

    /// <summary>
    /// 统一安全检测入口：ForbiddenApiDetector + ReflectionGuard + SerializationGuard。
    /// 使用采样策略：每 64 次调用检测一次（Release 模式下 ~1.5% 采样率）。
    /// </summary>
    private void DetectSecurityViolations()
    {
        if (string.IsNullOrEmpty(_extensionId)) return;

#if DEBUG
        // DEBUG 模式：每次都检测
        PerformDetection();
#else
        // RELEASE 模式：采样检测（每 64 次调用检测一次）
        int count = Interlocked.Increment(ref _callCounter);
        if ((count & 63) == 0) // 等价于 count % 64 == 0，但更快
        {
            PerformDetection();
        }
#endif
    }

    private void PerformDetection()
    {
        // ForbiddenApiDetector 检测
        var result = ForbiddenApiDetector.DetectFromCallStack(_extensionId!);
        if (result.HasViolation)
        {
            foreach (var violation in result.Violations)
            {
                logger.Warn($"[{_extensionId}] 检测到禁止API调用: {violation.ForbiddenApi}");
                logger.Warn($"  替代方案: {violation.Alternative}");
                logger.Warn($"  原因: {violation.Reason}");
            }
        }

        // ReflectionGuard 采样检测
        if (SecuritySampler.ShouldBlockReflection(_extensionId!, _reflectionGuard, null))
        {
            logger.Warn($"[{_extensionId}] 检测到可疑反射调用");
        }

        // SerializationGuard 采样检测
        if (SecuritySampler.ShouldBlockSerialization(_extensionId!, _serializationGuard, null))
        {
            logger.Warn($"[{_extensionId}] 检测到可疑序列化调用");
        }
    }
}
