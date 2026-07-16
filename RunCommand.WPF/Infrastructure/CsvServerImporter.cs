using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RunCommand.WPF.Models;

namespace RunCommand.WPF.Infrastructure
{
    /// <summary>
    /// Bulk-imports servers from a CSV file of connection strings. Two layouts are
    /// accepted:
    ///   1. With a header row containing a "ConnectionString" column (plus optional
    ///      "Name" / "Group" columns, any order):
    ///         Name,ConnectionString,Group
    ///         Store 0142,"Server=10.0.0.12,1433;Database=Pos;User Id=sa;Password=x;",Tehran
    ///   2. No header - a single column of raw connection strings, one per row
    ///      (e.g. exported from another tool with no headers at all).
    /// Quoted fields are supported (RFC4180-style) since connection strings
    /// routinely contain commas themselves (e.g. "host,port").
    /// </summary>
    public static class CsvServerImporter
    {
        public sealed class CsvImportResult
        {
            public ServerConnectionInfo? Info { get; init; }
            public string RawLine { get; init; } = "";
            public string? Error { get; init; }
        }

        /// <param name="csvContent">Full text of the CSV file.</param>
        /// <param name="defaultGroup">Applied to rows that don't have their own Group column value.</param>
        public static List<CsvImportResult> ParseCsv(string csvContent, string? defaultGroup = null)
        {
            var results = new List<CsvImportResult>();
            var rows = ParseRows(csvContent);
            if (rows.Count == 0) return results;

            var header = rows[0];
            int nameIdx = -1, connIdx = -1, groupIdx = -1;
            for (int i = 0; i < header.Count; i++)
            {
                var h = Normalize(header[i]);
                if (connIdx == -1 && h is "connectionstring" or "connstring" or "connection" or "conn" or "cs" or "rawconnectionstring")
                    connIdx = i;
                else if (nameIdx == -1 && h is "name" or "servername" or "displayname" or "storename")
                    nameIdx = i;
                else if (groupIdx == -1 && h is "group" or "region" or "district" or "store")
                    groupIdx = i;
            }

            bool hasHeader = connIdx != -1;
            int start = hasHeader ? 1 : 0;

            for (int r = start; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace)) continue;

                string raw;
                string? nameOverride = null;
                string? group = defaultGroup;

                if (hasHeader)
                {
                    raw = connIdx < row.Count ? row[connIdx] : "";
                    if (nameIdx != -1 && nameIdx < row.Count && !string.IsNullOrWhiteSpace(row[nameIdx]))
                        nameOverride = row[nameIdx].Trim();
                    if (groupIdx != -1 && groupIdx < row.Count && !string.IsNullOrWhiteSpace(row[groupIdx]))
                        group = row[groupIdx].Trim();
                }
                else
                {
                    // No recognizable header - treat the whole row as one field. Handles a
                    // plain single-column export where a connection string's own commas
                    // were not quoted, by rejoining the split pieces.
                    raw = string.Join(",", row);
                }

                raw = raw.Trim();
                if (string.IsNullOrWhiteSpace(raw)) continue;

                try
                {
                    var info = ConnectionStringParser.Parse(raw, nameOverride, group);
                    results.Add(new CsvImportResult { Info = info, RawLine = raw });
                }
                catch (Exception ex)
                {
                    results.Add(new CsvImportResult { Info = null, RawLine = raw, Error = ex.Message });
                }
            }

            return results;
        }

        private static string Normalize(string s) =>
            s.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");

        /// <summary>Minimal RFC4180 CSV reader: quoted fields, escaped "" quotes, embedded commas/newlines inside quotes.</summary>
        private static List<List<string>> ParseRows(string content)
        {
            var rows = new List<List<string>>();
            var field = new StringBuilder();
            var row = new List<string>();
            bool inQuotes = false;
            int i = 0;
            int n = content.Length;

            void EndField() { row.Add(field.ToString()); field.Clear(); }
            void EndRow() { EndField(); rows.Add(row); row = new List<string>(); }

            while (i < n)
            {
                char c = content[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < n && content[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                        inQuotes = false; i++; continue;
                    }
                    field.Append(c); i++; continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true; i++; break;
                    case ',':
                        EndField(); i++; break;
                    case '\r':
                        i++;
                        if (i < n && content[i] == '\n') i++;
                        EndRow(); break;
                    case '\n':
                        i++; EndRow(); break;
                    default:
                        field.Append(c); i++; break;
                }
            }
            if (field.Length > 0 || row.Count > 0) EndRow();

            // Drop blank trailing rows some editors/exports leave at end of file.
            return rows.Where(r => !(r.Count == 1 && string.IsNullOrWhiteSpace(r[0]))).ToList();
        }
    }
}
