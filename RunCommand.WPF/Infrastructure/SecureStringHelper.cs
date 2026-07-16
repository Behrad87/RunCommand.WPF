using System;
using System.Security.Cryptography;
using System.Text;

namespace RunCommand.WPF.Infrastructure
{
    /// <summary>
    /// Encrypts/decrypts passwords with Windows DPAPI (CurrentUser scope) before they
    /// touch the local SQLite file. Requires the NuGet package
    /// System.Security.Cryptography.ProtectedData on .NET 6+.
    /// NOTE: DPAPI keys are tied to the Windows user profile - the servers.db file is
    /// therefore only portable across machines if you re-enter passwords, which is the
    /// correct trade-off for "not stored on a server".
    /// </summary>
    public static class SecureStringHelper
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RunCommand.WPF.v1");

        public static string? Protect(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return null;

            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encrypted = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        public static string? Unprotect(string? encryptedBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64)) return null;

            try
            {
                var encryptedBytes = Convert.FromBase64String(encryptedBase64);
                var plainBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (CryptographicException)
            {
                // Encrypted on a different machine/user profile - caller should prompt to re-enter password.
                return null;
            }
        }
    }
}
