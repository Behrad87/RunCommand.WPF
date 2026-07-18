using System;
using Microsoft.Data.SqlClient;
using RunCommand.WPF.Infrastructure;

namespace RunCommand.WPF.Models
{
    /// <summary>
    /// Plain data record persisted to the local SQLite store.
    /// One row per retail-store SQL Server instance.
    /// </summary>
    public class ServerConnectionInfo
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;       // e.g. "Store 0142 - Tehran"
        public string HostName { get; set; } = string.Empty;   // Server name / Data Source (host, host,port, host\instance, or (localdb)\Instance)
        public int Port { get; set; } = 1433;                  // only used when HostName has no embedded port; ignored when RawConnectionString is set
        public string DatabaseName { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;      // region / district, useful for filtering thousands of rows

        public bool UseWindowsAuth { get; set; } = false;
        public string? UserName { get; set; }
        public string? EncryptedPassword { get; set; }         // DPAPI-protected, see SecureStringHelper
        public bool RememberPassword { get; set; } = true;

        /// <summary>Encrypt option: Mandatory, Optional, or Strict (matches SqlConnectionEncryptOption).</summary>
        public string Encrypt { get; set; } = "Mandatory";
        public bool TrustServerCertificate { get; set; } = true;

        /// <summary>
        /// When set, this is the source of truth for connecting - added so a full
        /// connection string (LocalDB, named instance, Azure SQL, etc.) can be pasted
        /// in as-is instead of being decomposed into Host/Port/Database. The password
        /// portion is stripped out and stored encrypted separately (see
        /// ConnectionStringParser), never as plain text in this field.
        /// </summary>
        public string? RawConnectionString { get; set; }

        public bool IsEnabled { get; set; } = true;             // allow disabling a store without deleting it

        // Runtime/status fields - also persisted so the grid shows last-known state on startup
        public ServerStatus Status { get; set; } = ServerStatus.Unknown;
        public DateTime? LastCheckedUtc { get; set; }
        public long? LastResponseTimeMs { get; set; }
        public string? LastError { get; set; }

        public string BuildConnectionString(int connectTimeoutSeconds = 3)
        {
            if (!string.IsNullOrWhiteSpace(RawConnectionString))
            {
                // Trust the pasted connection string as-is (covers LocalDB, named
                // instances, Azure SQL, etc.) - just override the timeout and fill in
                // the decrypted password if one was captured separately.
                var raw = new SqlConnectionStringBuilder(RawConnectionString)
                {
                    ConnectTimeout = connectTimeoutSeconds
                };
                if (!raw.IntegratedSecurity && !string.IsNullOrEmpty(EncryptedPassword))
                {
                    raw.Password = SecureStringHelper.Unprotect(EncryptedPassword) ?? raw.Password;
                }
                return raw.ConnectionString;
            }

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = BuildDataSource(),
                InitialCatalog = DatabaseName,
                ConnectTimeout = connectTimeoutSeconds,
                Encrypt = ParseEncrypt(Encrypt),
                TrustServerCertificate = TrustServerCertificate
            };

            if (UseWindowsAuth)
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.UserID = UserName ?? string.Empty;
                builder.Password = SecureStringHelper.Unprotect(EncryptedPassword) ?? string.Empty;
            }

            return builder.ConnectionString;
        }

        private string BuildDataSource()
        {
            // Server name may already include instance and/or port, e.g. host\Node,49149
            if (string.IsNullOrWhiteSpace(HostName)) return string.Empty;
            if (HostName.Contains(',') || Port <= 0 || Port == 1433)
                return HostName;
            return $"{HostName},{Port}";
        }

        public static SqlConnectionEncryptOption ParseEncrypt(string? value) =>
            (value ?? "Mandatory").Trim().ToLowerInvariant() switch
            {
                "optional" or "false" or "no" => SqlConnectionEncryptOption.Optional,
                "strict" => SqlConnectionEncryptOption.Strict,
                _ => SqlConnectionEncryptOption.Mandatory
            };
    }
}
