using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using RunCommand.WPF.Models;

namespace RunCommand.WPF.Infrastructure
{
    /// <summary>
    /// Local-only persistence for the server list. Uses SQLite (embedded, single file,
    /// no SQL Server instance required) stored under %AppData%. This is deliberately
    /// separate from any SQL Server database so the tool works even before any of the
    /// 1000 store servers are reachable.
    /// NuGet: Microsoft.Data.Sqlite
    /// </summary>
    public class LocalServerStore
    {
        private readonly string _connectionString;

        public LocalServerStore(string? dbFilePath = null)
        {
            var path = dbFilePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MultiServerQueryTool", "servers.db");

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _connectionString = $"Data Source={path}";
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS Servers (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    HostName TEXT NOT NULL,
                    Port INTEGER NOT NULL,
                    DatabaseName TEXT NOT NULL,
                    "Group" TEXT,
                    UseWindowsAuth INTEGER NOT NULL,
                    UserName TEXT,
                    EncryptedPassword TEXT,
                    IsEnabled INTEGER NOT NULL,
                    Status INTEGER NOT NULL,
                    LastCheckedUtc TEXT,
                    LastResponseTimeMs INTEGER,
                    LastError TEXT,
                    RawConnectionString TEXT,
                    Encrypt TEXT NOT NULL DEFAULT 'Mandatory',
                    TrustServerCertificate INTEGER NOT NULL DEFAULT 1,
                    RememberPassword INTEGER NOT NULL DEFAULT 1
                );
                CREATE INDEX IF NOT EXISTS IX_Servers_Group ON Servers("Group");
                """;
            cmd.ExecuteNonQuery();

            TryAddColumn(conn, "RawConnectionString TEXT");
            TryAddColumn(conn, "Encrypt TEXT NOT NULL DEFAULT 'Mandatory'");
            TryAddColumn(conn, "TrustServerCertificate INTEGER NOT NULL DEFAULT 1");
            TryAddColumn(conn, "RememberPassword INTEGER NOT NULL DEFAULT 1");
        }

        private static void TryAddColumn(SqliteConnection conn, string columnDef)
        {
            try
            {
                var alter = conn.CreateCommand();
                alter.CommandText = $"ALTER TABLE Servers ADD COLUMN {columnDef};";
                alter.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
                // Column already exists - fine, ignore.
            }
        }

        public async Task<List<ServerConnectionInfo>> GetAllAsync()
        {
            var result = new List<ServerConnectionInfo>();
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Servers ORDER BY Name;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(Map(reader));
            }
            return result;
        }

        public async Task UpsertAsync(ServerConnectionInfo server)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Servers
                    (Id, Name, HostName, Port, DatabaseName, "Group", UseWindowsAuth, UserName,
                     EncryptedPassword, IsEnabled, Status, LastCheckedUtc, LastResponseTimeMs, LastError,
                     RawConnectionString, Encrypt, TrustServerCertificate, RememberPassword)
                VALUES
                    ($Id, $Name, $HostName, $Port, $DatabaseName, $Group, $UseWindowsAuth, $UserName,
                     $EncryptedPassword, $IsEnabled, $Status, $LastCheckedUtc, $LastResponseTimeMs, $LastError,
                     $RawConnectionString, $Encrypt, $TrustServerCertificate, $RememberPassword)
                ON CONFLICT(Id) DO UPDATE SET
                    Name=$Name, HostName=$HostName, Port=$Port, DatabaseName=$DatabaseName, "Group"=$Group,
                    UseWindowsAuth=$UseWindowsAuth, UserName=$UserName, EncryptedPassword=$EncryptedPassword,
                    IsEnabled=$IsEnabled, Status=$Status, LastCheckedUtc=$LastCheckedUtc,
                    LastResponseTimeMs=$LastResponseTimeMs, LastError=$LastError, RawConnectionString=$RawConnectionString,
                    Encrypt=$Encrypt, TrustServerCertificate=$TrustServerCertificate, RememberPassword=$RememberPassword;
                """;
            Bind(cmd, server);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Servers WHERE Id = $Id;";
            cmd.Parameters.AddWithValue("$Id", id.ToString());
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Bulk-persist status after a health-check sweep (single transaction, fast for 1000+ rows).</summary>
        public async Task UpdateStatusBatchAsync(IEnumerable<ServerConnectionInfo> servers)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();
            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE Servers SET Status=$Status, LastCheckedUtc=$LastCheckedUtc,
                       LastResponseTimeMs=$LastResponseTimeMs, LastError=$LastError
                WHERE Id=$Id;
                """;
            var pStatus = cmd.Parameters.Add("$Status", SqliteType.Integer);
            var pChecked = cmd.Parameters.Add("$LastCheckedUtc", SqliteType.Text);
            var pRt = cmd.Parameters.Add("$LastResponseTimeMs", SqliteType.Integer);
            var pErr = cmd.Parameters.Add("$LastError", SqliteType.Text);
            var pId = cmd.Parameters.Add("$Id", SqliteType.Text);

            foreach (var s in servers)
            {
                pStatus.Value = (int)s.Status;
                pChecked.Value = (object?)s.LastCheckedUtc?.ToString("O") ?? DBNull.Value;
                pRt.Value = (object?)s.LastResponseTimeMs ?? DBNull.Value;
                pErr.Value = (object?)s.LastError ?? DBNull.Value;
                pId.Value = s.Id.ToString();
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        private static void Bind(SqliteCommand cmd, ServerConnectionInfo s)
        {
            cmd.Parameters.AddWithValue("$Id", s.Id.ToString());
            cmd.Parameters.AddWithValue("$Name", s.Name);
            cmd.Parameters.AddWithValue("$HostName", s.HostName);
            cmd.Parameters.AddWithValue("$Port", s.Port);
            cmd.Parameters.AddWithValue("$DatabaseName", s.DatabaseName);
            cmd.Parameters.AddWithValue("$Group", (object?)s.Group ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$UseWindowsAuth", s.UseWindowsAuth ? 1 : 0);
            cmd.Parameters.AddWithValue("$UserName", (object?)s.UserName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$EncryptedPassword", (object?)s.EncryptedPassword ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$IsEnabled", s.IsEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$Status", (int)s.Status);
            cmd.Parameters.AddWithValue("$LastCheckedUtc", (object?)s.LastCheckedUtc?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$LastResponseTimeMs", (object?)s.LastResponseTimeMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$LastError", (object?)s.LastError ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$RawConnectionString", (object?)s.RawConnectionString ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$Encrypt", s.Encrypt);
            cmd.Parameters.AddWithValue("$TrustServerCertificate", s.TrustServerCertificate ? 1 : 0);
            cmd.Parameters.AddWithValue("$RememberPassword", s.RememberPassword ? 1 : 0);
        }

        private static ServerConnectionInfo Map(SqliteDataReader r) => new()
        {
            Id = Guid.Parse(r.GetString(r.GetOrdinal("Id"))),
            Name = r.GetString(r.GetOrdinal("Name")),
            HostName = r.GetString(r.GetOrdinal("HostName")),
            Port = r.GetInt32(r.GetOrdinal("Port")),
            DatabaseName = r.GetString(r.GetOrdinal("DatabaseName")),
            Group = r.IsDBNull(r.GetOrdinal("Group")) ? "" : r.GetString(r.GetOrdinal("Group")),
            UseWindowsAuth = r.GetInt32(r.GetOrdinal("UseWindowsAuth")) == 1,
            UserName = r.IsDBNull(r.GetOrdinal("UserName")) ? null : r.GetString(r.GetOrdinal("UserName")),
            EncryptedPassword = r.IsDBNull(r.GetOrdinal("EncryptedPassword")) ? null : r.GetString(r.GetOrdinal("EncryptedPassword")),
            IsEnabled = r.GetInt32(r.GetOrdinal("IsEnabled")) == 1,
            Status = (ServerStatus)r.GetInt32(r.GetOrdinal("Status")),
            LastCheckedUtc = r.IsDBNull(r.GetOrdinal("LastCheckedUtc")) ? null : DateTime.Parse(r.GetString(r.GetOrdinal("LastCheckedUtc"))),
            LastResponseTimeMs = r.IsDBNull(r.GetOrdinal("LastResponseTimeMs")) ? null : r.GetInt64(r.GetOrdinal("LastResponseTimeMs")),
            LastError = r.IsDBNull(r.GetOrdinal("LastError")) ? null : r.GetString(r.GetOrdinal("LastError")),
            RawConnectionString = r.IsDBNull(r.GetOrdinal("RawConnectionString")) ? null : r.GetString(r.GetOrdinal("RawConnectionString")),
            Encrypt = HasColumn(r, "Encrypt") && !r.IsDBNull(r.GetOrdinal("Encrypt"))
                ? r.GetString(r.GetOrdinal("Encrypt")) : "Mandatory",
            TrustServerCertificate = !HasColumn(r, "TrustServerCertificate") || r.IsDBNull(r.GetOrdinal("TrustServerCertificate"))
                || r.GetInt32(r.GetOrdinal("TrustServerCertificate")) == 1,
            RememberPassword = !HasColumn(r, "RememberPassword") || r.IsDBNull(r.GetOrdinal("RememberPassword"))
                || r.GetInt32(r.GetOrdinal("RememberPassword")) == 1,
        };

        private static bool HasColumn(SqliteDataReader r, string name)
        {
            for (int i = 0; i < r.FieldCount; i++)
                if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
