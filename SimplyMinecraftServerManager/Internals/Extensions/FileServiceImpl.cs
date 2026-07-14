// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.IO;
using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// IFileService 实现。所有文件操作经过 FileAccessGuard 校验。
/// </summary>
internal sealed class FileServiceImpl(FileAccessGuard guard, ILogger logger) : IFileService
{
    public async Task<FileReadResult> ReadBytesAsync(string scopeId, string relativePath, CancellationToken ct = default)
    {
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
            return FileReadResult.Fail(path, ex.Message);
        }
    }

    public async Task<FileReadResult> ReadTextAsync(string scopeId, string relativePath, CancellationToken ct = default)
    {
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
            return FileReadResult.Fail(path, ex.Message);
        }
    }

    public async Task<FileOperationResult> WriteBytesAsync(string scopeId, string relativePath, byte[] data, CancellationToken ct = default)
    {
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
            return FileOperationResult.Fail(path, ex.Message);
        }
    }

    public async Task<FileOperationResult> WriteTextAsync(string scopeId, string relativePath, string text, CancellationToken ct = default)
    {
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
            return FileOperationResult.Fail(path, ex.Message);
        }
    }

    public async Task<FileOperationResult> AppendTextAsync(string scopeId, string relativePath, string text, CancellationToken ct = default)
    {
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
            return FileOperationResult.Fail(path, ex.Message);
        }
    }

    public Task<FileOperationResult> DeleteAsync(string scopeId, string relativePath, CancellationToken ct = default)
    {
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
            return Task.FromResult(FileOperationResult.Fail(path, ex.Message));
        }
    }

    public Task<FileOperationResult> MoveAsync(string scopeId, string sourcePath, string destPath, CancellationToken ct = default)
    {
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
            return Task.FromResult(FileOperationResult.Fail(sourcePath, ex.Message));
        }
    }

    public Task<FileOperationResult> CopyAsync(string scopeId, string sourcePath, string destPath, CancellationToken ct = default)
    {
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
            return Task.FromResult(FileOperationResult.Fail(sourcePath, ex.Message));
        }
    }

    public Task<bool> ExistsAsync(string scopeId, string relativePath, CancellationToken ct = default)
    {
        string? path = guard.Validate(scopeId, relativePath, FileAccessLevel.Read);
        if (path is null)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(File.Exists(path));
    }

    public Task<FileOperationResult> GetFileInfoAsync(string scopeId, string relativePath, CancellationToken ct = default)
    {
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
            return Task.FromResult(FileOperationResult.Fail(path, ex.Message));
        }
    }
}
