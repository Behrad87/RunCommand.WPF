using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using RunCommand.WPF.Models;

namespace RunCommand.WPF.Services
{
    /// <summary>
    /// Checks reachability of many SQL Server instances in parallel, throttled so
    /// 1000 servers don't open 1000 sockets at once. NuGet: Microsoft.Data.SqlClient
    /// </summary>
    public class ServerHealthChecker
    {
        /// <param name="servers">Servers to check.</param>
        /// <param name="maxConcurrency">Cap on simultaneous connection attempts (tune to your network - 30-50 is a safe start for WAN links to stores).</param>
        /// <param name="progress">Reports each server as soon as its check completes, so the grid updates incrementally instead of waiting for all 1000.</param>
        public async Task CheckAllAsync(
            IEnumerable<ServerConnectionInfo> servers,
            int maxConcurrency,
            IProgress<ServerConnectionInfo>? progress,
            CancellationToken cancellationToken)
        {
            using var throttle = new SemaphoreSlim(maxConcurrency);
            var tasks = servers.Select(async server =>
            {
                await throttle.WaitAsync(cancellationToken);
                try
                {
                    await CheckOneAsync(server, cancellationToken);
                }
                finally
                {
                    throttle.Release();
                    progress?.Report(server);
                }
            });

            await Task.WhenAll(tasks);
        }

        public async Task CheckOneAsync(ServerConnectionInfo server, CancellationToken cancellationToken)
        {
            server.Status = ServerStatus.Checking;
            var sw = Stopwatch.StartNew();
            try
            {
                await using var conn = new SqlConnection(server.BuildConnectionString(connectTimeoutSeconds: 3));
                await conn.OpenAsync(cancellationToken);

                // Cheap round trip to confirm the DB (not just the box) responds.
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1;";
                cmd.CommandTimeout = 3;
                await cmd.ExecuteScalarAsync(cancellationToken);

                sw.Stop();
                server.Status = ServerStatus.Online;
                server.LastResponseTimeMs = sw.ElapsedMilliseconds;
                server.LastError = null;
            }
            catch (SqlException ex) when (IsAuthError(ex))
            {
                sw.Stop();
                server.Status = ServerStatus.AuthFailed;
                server.LastResponseTimeMs = sw.ElapsedMilliseconds;
                server.LastError = ex.Message;
            }
            catch (Exception ex)
            {
                sw.Stop();
                server.Status = ServerStatus.Offline;
                server.LastResponseTimeMs = sw.ElapsedMilliseconds;
                server.LastError = ex.Message;
            }
            finally
            {
                server.LastCheckedUtc = DateTime.UtcNow;
            }
        }

        private static bool IsAuthError(SqlException ex) =>
            ex.Number is 18456 or 18452 or 4060; // login failed / not associated / invalid database
    }
}
