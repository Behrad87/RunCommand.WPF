using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using RunCommand.WPF.Models;

namespace RunCommand.WPF.Services
{
    public class MultiServerQueryRunner
    {
        /// <param name="servers">Selected servers to run against.</param>
        /// <param name="sql">Query or command text.</param>
        /// <param name="maxConcurrency">Throttle - keep well under 1000; 20-50 is a reasonable default for WAN-connected retail stores.</param>
        /// <param name="commandTimeoutSeconds">Per-server SQL timeout.</param>
        /// <param name="captureFullResultSet">
        /// If true, keeps the full DataTable per server (fine for small selections, e.g. under ~50 servers).
        /// If false, only row count + duration is kept, which is what you want when running
        /// against hundreds/thousands of servers at once to avoid holding gigabytes of data tables in memory.
        /// </param>
        /// <param name="progress">Reports each server's result as it finishes, so the results grid fills in live.</param>
        public async Task<List<QueryExecutionResult>> ExecuteAsync(
            IEnumerable<ServerConnectionInfo> servers,
            string sql,
            int maxConcurrency,
            int commandTimeoutSeconds,
            bool captureFullResultSet,
            IProgress<QueryExecutionResult>? progress,
            CancellationToken cancellationToken)
        {
            using var throttle = new SemaphoreSlim(maxConcurrency);
            var results = new List<QueryExecutionResult>();
            var resultsLock = new object();

            var tasks = servers.Select(async server =>
            {
                await throttle.WaitAsync(cancellationToken);
                try
                {
                    var result = await ExecuteOneAsync(server, sql, commandTimeoutSeconds, captureFullResultSet, cancellationToken);
                    lock (resultsLock) results.Add(result);
                    progress?.Report(result);
                }
                finally
                {
                    throttle.Release();
                }
            });

            await Task.WhenAll(tasks);
            return results;
        }

        private static async Task<QueryExecutionResult> ExecuteOneAsync(
            ServerConnectionInfo server,
            string sql,
            int commandTimeoutSeconds,
            bool captureFullResultSet,
            CancellationToken cancellationToken)
        {
            var result = new QueryExecutionResult
            {
                ServerId = server.Id,
                ServerName = server.Name,
                HostName = server.HostName
            };

            var sw = Stopwatch.StartNew();
            try
            {
                await using var conn = new SqlConnection(server.BuildConnectionString(connectTimeoutSeconds: 5));
                await conn.OpenAsync(cancellationToken);

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = commandTimeoutSeconds;

                if (captureFullResultSet)
                {
                    var table = new DataTable();
                    using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    table.Load(reader);
                    result.Data = table;
                    result.RowsAffectedOrReturned = table.Rows.Count;
                }
                else
                {
                    // Still need to know if it was a SELECT (row count) or DML (rows affected).
                    if (sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                    {
                        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                        int count = 0;
                        while (await reader.ReadAsync(cancellationToken)) count++;
                        result.RowsAffectedOrReturned = count;
                    }
                    else
                    {
                        result.RowsAffectedOrReturned = await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
            }
            finally
            {
                sw.Stop();
                result.DurationMs = sw.ElapsedMilliseconds;
            }

            return result;
        }
    }
}
