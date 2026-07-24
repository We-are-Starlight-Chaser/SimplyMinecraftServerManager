// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

namespace SimplyMinecraftServerManager.Extension.Interfaces;

/// <summary>
/// 哈希/校验服务，供扩展安全地计算文件和数据的哈希值。
/// 禁止扩展直接使用 System.Security.Cryptography。
/// </summary>
public interface IHashService
{
    /// <summary>计算文件的 SHA256 哈希值</summary>
    Task<string> ComputeFileHashAsync(string filePath, CancellationToken ct = default);

    /// <summary>计算字节数据的 SHA256 哈希值</summary>
    string ComputeSha256(byte[] data);

    /// <summary>计算字符串的 SHA256 哈希值</summary>
    string ComputeSha256(string text);

    /// <summary>验证文件哈希是否匹配</summary>
    Task<bool> VerifyFileHashAsync(string filePath, string expectedHash, CancellationToken ct = default);

    /// <summary>生成加密安全的随机字节</summary>
    byte[] GenerateRandomBytes(int count);

    /// <summary>生成加密安全的随机字符串（Base64 URL）</summary>
    string GenerateRandomString(int byteCount = 32);
}
