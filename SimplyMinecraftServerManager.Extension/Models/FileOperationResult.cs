// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Models;

/// <summary>
/// 文件操作结果。
/// </summary>
public class FileOperationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? FilePath { get; init; }

    public static FileOperationResult Ok(string path) => new() { Success = true, FilePath = path };
    public static FileOperationResult Fail(string path, string error) =>
        new() { Success = false, FilePath = path, ErrorMessage = error };
}

/// <summary>
/// 文件读取结果。
/// </summary>
public sealed class FileReadResult : FileOperationResult
{
    public byte[]? Data { get; init; }
    public string? Text { get; init; }
    public long SizeBytes { get; init; }

    public static FileReadResult Ok(string path, byte[] data) =>
        new() { Success = true, FilePath = path, Data = data, SizeBytes = data.Length };
    public static FileReadResult OkText(string path, string text) =>
        new() { Success = true, FilePath = path, Text = text, SizeBytes = text.Length * 2 };
    public new static FileReadResult Fail(string path, string error) =>
        new() { Success = false, FilePath = path, ErrorMessage = error };
}

/// <summary>
/// 目录列举结果。
/// </summary>
public sealed class DirectoryListResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string[] Files { get; init; } = [];
    public string[] Directories { get; init; } = [];

    public static DirectoryListResult Ok(string[] files, string[] directories) =>
        new() { Success = true, Files = files, Directories = directories };
    public static DirectoryListResult Fail(string error) =>
        new() { Success = false, ErrorMessage = error };
}
