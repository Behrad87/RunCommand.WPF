using System;
using System.Data;

namespace RunCommand.WPF.Models
{
    /// <summary>
    /// Result of running one query against one server. Kept lightweight (no full
    /// DataTable by default) so running against 1000+ servers doesn't exhaust memory -
    /// a preview + row count is stored, full DataTable is optional (see Runner options).
    /// </summary>
    public class QueryExecutionResult
    {
        public Guid ServerId { get; set; }
        public string ServerName { get; set; } = string.Empty;
        public string HostName { get; set; } = string.Empty;

        public bool Success { get; set; }
        public int RowsAffectedOrReturned { get; set; }
        public long DurationMs { get; set; }
        public string? Error { get; set; }

        /// <summary>Only populated when the caller asks for full results (small server sets).</summary>
        public DataTable? Data { get; set; }
    }
}
