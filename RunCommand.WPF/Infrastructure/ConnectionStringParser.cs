using System;
using Microsoft.Data.SqlClient;

using RunCommand.WPF.Models;

namespace RunCommand.WPF.Infrastructure
{
    /// <summary>
    /// Parses a pasted ADO.NET connection string (LocalDB, named instance, host,port,
    /// Azure SQL, etc.) into a ServerConnectionInfo. The connection string is kept as
    /// the source of truth in RawConnectionString, but with the password stripped out -
    /// it's captured once, encrypted via SecureStringHelper, and stored separately so
    /// nothing lands on disk in plain text.
    /// </summary>
    public static class ConnectionStringParser
    {
        public static ServerConnectionInfo Parse(string connectionString, string? nameOverride = null, string? group = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string is empty.", nameof(connectionString));

            var builder = new SqlConnectionStringBuilder(connectionString.Trim());

            var info = new ServerConnectionInfo
            {
                HostName = builder.DataSource,
                DatabaseName = builder.InitialCatalog,
                Group = group ?? string.Empty,
                UseWindowsAuth = builder.IntegratedSecurity,
                UserName = builder.IntegratedSecurity ? null : builder.UserID,
                EncryptedPassword = builder.IntegratedSecurity ? null : SecureStringHelper.Protect(builder.Password),
            };

            // Strip the password out of the stored connection string - it's re-applied
            // from EncryptedPassword at connect time in ServerConnectionInfo.BuildConnectionString.
            if (!builder.IntegratedSecurity)
                builder.Password = string.Empty;

            info.RawConnectionString = builder.ConnectionString;
            info.Name = string.IsNullOrWhiteSpace(nameOverride) ? DeriveName(builder.DataSource, builder.InitialCatalog) : nameOverride;

            return info;
        }

        private static string DeriveName(string dataSource, string database)
        {
            var cleaned = dataSource
                .Replace(@"(localdb)\", "LocalDB-", StringComparison.OrdinalIgnoreCase)
                .Replace('\\', '-');

            return string.IsNullOrWhiteSpace(database) || database.Equals("master", StringComparison.OrdinalIgnoreCase)
                ? cleaned
                : $"{cleaned} ({database})";
        }
    }
}
