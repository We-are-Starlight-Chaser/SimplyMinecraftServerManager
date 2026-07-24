// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.IO;
using System.Security.Cryptography;
using System.Text;
using SimplyMinecraftServerManager.Extension.Interfaces;

namespace SimplyMinecraftServerManager.Internals.Extensions;

/// <summary>
/// IHashService 实现，基于 System.Security.Cryptography 提供安全的哈希和随机数服务。
/// 扩展无需直接引用 System.Security.Cryptography。
/// </summary>
internal sealed class HashServiceImpl : IHashService
{
    public string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    public string ComputeSha256(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return ComputeSha256(bytes);
    }

    public async Task<string> ComputeFileHashAsync(string filePath, CancellationToken ct = default)
    {
        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            incrementalHash.AppendData(buffer, 0, bytesRead);
        }
        return Convert.ToHexStringLower(incrementalHash.GetHashAndReset());
    }

    public async Task<bool> VerifyFileHashAsync(string filePath, string expectedHash, CancellationToken ct = default)
    {
        var actual = await ComputeFileHashAsync(filePath, ct).ConfigureAwait(false);
        return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    public byte[] GenerateRandomBytes(int count)
    {
        var buffer = new byte[count];
        RandomNumberGenerator.Fill(buffer);
        return buffer;
    }

    public string GenerateRandomString(int byteCount = 32)
    {
        var bytes = GenerateRandomBytes(byteCount);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
