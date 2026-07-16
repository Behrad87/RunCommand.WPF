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
        public string HostName { get; set; } = string.Empty;   // display value - "Data Source" as typed (host, host,port, host\instance, or (localdb)\Instance)
        public int Port { get; set; } = 1433;                  // only used when building from parts (manual entry); ignored when RawConnectionString is set
        public string DatabaseName { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;      // region / district, useful for filtering thousands of rows

        public bool UseWindowsAuth { get; set; } = false;
        public string? UserName { get; set; }
        public string? EncryptedPassword { get; set; }         // DPAPI-protected, see SecureStringHelper

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
                DataSource = Port is <= 0 or 1433 ? HostName : $"{HostName},{Port}",
                InitialCatalog = DatabaseName,
                ConnectTimeout = connectTimeoutSeconds,
                TrustServerCertificate = true
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
    }
}
