// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.Extension.Models;

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 文件操作服务。扩展必须通过此接口访问文件，禁止直接使用 System.IO。
/// 所有操作都会经过 FileAccessGuard 校验：路径合法性 → 声明范围 → 危险过滤 → 文件类型。
/// </summary>
public interface IFileService
{
    /// <summary>读取文件内容（字节）</summary>
    Task<FileReadResult> ReadBytesAsync(string scopeId, string relativePath, CancellationToken ct = default);

    /// <summary>读取文件内容（文本）</summary>
    Task<FileReadResult> ReadTextAsync(string scopeId, string relativePath, CancellationToken ct = default);

    /// <summary>写入文件（创建或覆盖）</summary>
    Task<FileOperationResult> WriteBytesAsync(string scopeId, string relativePath, byte[] data, CancellationToken ct = default);

    /// <summary>写入文件（文本，UTF-8）</summary>
    Task<FileOperationResult> WriteTextAsync(string scopeId, string relativePath, string text, CancellationToken ct = default);

    /// <summary>追加写入</summary>
    Task<FileOperationResult> AppendTextAsync(string scopeId, string relativePath, string text, CancellationToken ct = default);

    /// <summary>删除文件</summary>
    Task<FileOperationResult> DeleteAsync(string scopeId, string relativePath, CancellationToken ct = default);

    /// <summary>移动文件</summary>
    Task<FileOperationResult> MoveAsync(string scopeId, string sourcePath, string destPath, CancellationToken ct = default);

    /// <summary>复制文件</summary>
    Task<FileOperationResult> CopyAsync(string scopeId, string sourcePath, string destPath, CancellationToken ct = default);

    /// <summary>检查文件是否存在</summary>
    Task<bool> ExistsAsync(string scopeId, string relativePath, CancellationToken ct = default);

    /// <summary>获取文件信息</summary>
    Task<FileOperationResult> GetFileInfoAsync(string scopeId, string relativePath, CancellationToken ct = default);
}
