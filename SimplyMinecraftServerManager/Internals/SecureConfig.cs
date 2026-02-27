using System;
using System.Security.Cryptography;
using System.Text;

namespace SimplyMinecraftServerManager.Internals
{
    public static class SecureConfig
    {
        private static readonly byte[] EntropyBytes = Encoding.UTF8.GetBytes("SimplyMinecraftServerManager_v1");
        private const string Prefix = "DPAPI:";

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
            catch (Exception)
            {
                return plainText;
            }
        }

        public static string Decrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText)) return string.Empty;

            if (!encryptedText.StartsWith(Prefix))
                return encryptedText;

            try
            {
                string base64 = encryptedText.Substring(Prefix.Length);
                byte[] encryptedBytes = Convert.FromBase64String(base64);
                byte[] plainBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    EntropyBytes,
                    DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception)
            {
                return encryptedText;
            }
        }

        public static bool TryDecrypt(string encryptedText, out string plainText)
        {
            plainText = Decrypt(encryptedText);
            return plainText != encryptedText;
        }
    }
}
