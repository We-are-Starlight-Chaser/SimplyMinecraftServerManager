// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.IO;
using SimplyMinecraftServerManager.Extension.Interfaces;
using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// IFolderService 实现。所有目录操作经过 FileAccessGuard 校验。
/// </summary>
internal sealed class FolderServiceImpl(FileAccessGuard guard, ILogger logger) : IFolderService
{
    public Task<DirectoryListResult> ListAsync(string scopeId, string relativePath, CancellationToken ct = default)
    {
        string? path = guard.Validate(scopeId, relativePath, FileAccessLevel.Read);
        if (path is null)
        {
            return Task.FromResult(DirectoryListResult.Fail("访问被拒绝"));
        }

        try
        {
            if (!Directory.Exists(path))
            {
                return Task.FromResult(DirectoryListResult.Fail($"目录不存在: {path}"));
            }

            string[] files = Directory.GetFiles(path)
                .Select(f => Path.GetFileName(f))
                .Where(n => n is not null)
                .ToArray()!;

            string[] dirs = Directory.GetDirectories(path)
                .Select(d => Path.GetFileName(d))
                .Where(n => n is not null)
                .ToArray()!;

            return Task.FromResult(DirectoryListResult.Ok(files, dirs));
        }
        catch (Exception ex)
        {
            logger.Error($"列举目录失败: {path}", ex);
            return Task.FromResult(DirectoryListResult.Fail(ex.Message));
        }
    }

    public Task<FileOperationResult> CreateAsync(string scopeId, string relativePath, CancellationToken ct = default)
    {
        string? path = guard.Validate(scopeId, relativePath, FileAccessLevel.Write);
        if (path is null)
        {
            return Task.FromResult(FileOperationResult.Fail(relativePath, "访问被拒绝"));
        }

        try
        {
            Directory.CreateDirectory(path);
            return Task.FromResult(FileOperationResult.Ok(path));
        }
        catch (Exception ex)
        {
            logger.Error($"创建目录失败: {path}", ex);
            return Task.FromResult(FileOperationResult.Fail(path, ex.Message));
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
            if (Directory.Exists(path))
            {
                // 仅允许删除空目录（安全考虑）
                if (Directory.GetFileSystemEntries(path).Length > 0)
                {
                    return Task.FromResult(FileOperationResult.Fail(path, "目录非空，拒绝删除"));
                }

                Directory.Delete(path);
            }
            return Task.FromResult(FileOperationResult.Ok(path));
        }
        catch (Exception ex)
        {
            logger.Error($"删除目录失败: {path}", ex);
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
            Directory.Move(resolvedSource, resolvedDest);
            return Task.FromResult(FileOperationResult.Ok(resolvedDest));
        }
        catch (Exception ex)
        {
            logger.Error($"移动目录失败: {sourcePath} → {destPath}", ex);
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

        return Task.FromResult(Directory.Exists(path));
    }

    public Task<long> GetSizeAsync(string scopeId, string relativePath, CancellationToken ct = default)
    {
        string? path = guard.Validate(scopeId, relativePath, FileAccessLevel.Read);
        if (path is null)
        {
            return Task.FromResult(0L);
        }

        try
        {
            if (!Directory.Exists(path))
            {
                return Task.FromResult(0L);
            }

            long size = Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try { return new FileInfo(file).Length; }
                    catch { return 0L; }
                });

            return Task.FromResult(size);
        }
        catch (Exception ex)
        {
            logger.Error($"计算目录大小失败: {path}", ex);
            return Task.FromResult(0L);
        }
    }
}
