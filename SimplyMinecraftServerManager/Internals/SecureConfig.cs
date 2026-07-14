// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;

namespace SimplyMinecraftServerManager.Internals
{
    /// <summary>
    /// 安全配置管理类，使用 Windows DPAPI 提供敏感数据的加密和解密功能。
    /// </summary>
    public static class SecureConfig
    {
        private static readonly byte[] EntropyBytes = Encoding.UTF8.GetBytes("SimplyMinecraftServerManager_v1");
        private const string Prefix = "DPAPI:";

        /// <summary>
        /// 使用 DPAPI 加密明文字符串。
        /// </summary>
        /// <param name="plainText">要加密的明文字符串。</param>
        /// <returns>加密后的字符串，如果加密失败则返回原始明文。</returns>
        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] encryptedBytes = ProtectedData.Protect(
                    plainBytes,
                    EntropyBytes,
                    DataProtectionScope.CurrentUser);
                return Prefix + Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SecureConfig] Encryption failed, storing as plaintext: {ex.Message}");
                return plainText;
            }
        }

        /// <summary>
        /// 使用 DPAPI 解密加密的字符串。
        /// </summary>
        /// <param name="encryptedText">要解密的加密字符串。</param>
        /// <returns>解密后的明文字符串，如果解密失败则返回原始字符串。</returns>
        public static string Decrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText)) return string.Empty;

            if (!encryptedText.StartsWith(Prefix))
                return encryptedText;

            try
            {
                string base64 = encryptedText[Prefix.Length..];
                byte[] encryptedBytes = Convert.FromBase64String(base64);
                byte[] plainBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    EntropyBytes,
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SecureConfig] Decryption failed: {ex.Message}");
                return encryptedText;
            }
        }


    }
}
