// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 目录操作服务。扩展必须通过此接口访问目录，禁止直接使用 System.IO。
/// </summary>
public interface IFolderService
{
    /// <summary>列举目录内容</summary>
    Task<DirectoryListResult> ListAsync(string scopeId, string relativePath = "", CancellationToken ct = default);

    /// <summary>创建目录</summary>
    Task<FileOperationResult> CreateAsync(string scopeId, string relativePath, CancellationToken ct = default);

    /// <summary>删除目录（必须为空）</summary>
    Task<FileOperationResult> DeleteAsync(string scopeId, string relativePath, CancellationToken ct = default);

    /// <summary>移动目录</summary>
    Task<FileOperationResult> MoveAsync(string scopeId, string sourcePath, string destPath, CancellationToken ct = default);

    /// <summary>检查目录是否存在</summary>
    Task<bool> ExistsAsync(string scopeId, string relativePath = "", CancellationToken ct = default);

    /// <summary>获取目录总大小（字节）</summary>
    Task<long> GetSizeAsync(string scopeId, string relativePath = "", CancellationToken ct = default);
}
