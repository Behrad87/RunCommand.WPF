# Multi-Server Query — integrated into RunCommand.WPF

The multi-server query page has been merged directly into this project
(namespace `RunCommand.WPF`, matching your project). On launch, `MainWindow`
now hosts a `Frame` that navigates straight to `Views/MultiServerQueryPage.xaml`.

## Before you build

1. **Target framework**: the project file specifies `net11.0-windows`. If that
   was a typo (e.g. meant `net8.0-windows`), fix it in `RunCommand.WPF.csproj`
   before restoring — `Microsoft.Data.Sqlite` / `Microsoft.Data.SqlClient`
   package versions referenced below target .NET 8.
2. **Restore NuGet packages** (Visual Studio will do this automatically on
   open, or run `dotnet restore` — this sandbox has no network access to
   nuget.org so packages could not be restored/built here):
   - `Microsoft.Data.Sqlite` — local server list storage
   - `Microsoft.Data.SqlClient` — connecting to each store's SQL Server
   - `System.Security.Cryptography.ProtectedData` — DPAPI password encryption

## What's new

```
Models/            ServerConnectionInfo, ServerStatus, QueryExecutionResult
Infrastructure/    LocalServerStore (SQLite), SecureStringHelper (DPAPI)
Services/          ServerHealthChecker, MultiServerQueryRunner
ViewModels/        RelayCommand, ServerItemViewModel, MultiServerQueryViewModel
Views/             MultiServerQueryPage (.xaml/.xaml.cs), AddServerDialog (.xaml/.xaml.cs)
Converters/        StatusToBrushConverter
MainWindow.xaml(.cs)  now hosts the page in a Frame instead of an empty Grid
```

See the in-code comments (especially `MultiServerQueryViewModel`,
`ServerHealthChecker`, `MultiServerQueryRunner`) for the throttling and
virtualization details — same design as discussed: local SQLite storage
(not SQL Server), throttled parallel health checks/queries so 1000 servers
don't open 1000 sockets at once, virtualized DataGrid, and color-coded
status (gray/amber/green/orange/red).

## Update: add servers by pasting connection string(s)

`Views/AddServerDialog` now has two tabs - Manual Entry (unchanged) and
"From Connection String(s)". Paste one connection string per line (LocalDB,
named instances, host,port, Azure SQL - anything SqlConnectionStringBuilder
accepts), each line parses live with a status dot, "Test All" checks them,
"Add All Parsed" commits them. New file: `Infrastructure/ConnectionStringParser.cs`.
`LocalServerStore` gained a `RawConnectionString` column with an automatic
migration for any `servers.db` created before this change.

## Update: bulk-import servers from a CSV file

The "From Connection String(s)" tab now also has an **"Import CSV..."** button
next to "Test All". Click it, pick a `.csv` file, and every row is parsed the
same way as a pasted line and appended to the preview grid (a "From" column
shows whether each row came from the textbox or a specific CSV file, so
importing a CSV doesn't wipe out anything already pasted or previously
imported). Review/"Test All"/"Add All Parsed" work exactly as before.

Two CSV layouts are accepted:

1. **With a header row** containing a `ConnectionString` column (column names
   are matched case-insensitively; `ConnString`, `Connection`, `Conn`, `CS`
   also work), plus optional `Name` and `Group`/`Region` columns, in any
   order:

   ```csv
   Name,ConnectionString,Group
   Store 0142,"Server=10.0.0.12,1433;Database=Pos;User Id=sa;Password=x;",Tehran
   Store 0143,"Server=10.0.0.13,1433;Database=Pos;Integrated Security=true;",Tehran
   ```

   Quote the `ConnectionString` field if it contains a comma (e.g. the common
   `host,port` form) - the importer uses a proper RFC4180-style CSV reader,
   not a naive `Split(',')`, so quoted commas are handled correctly.

2. **No header** - a single column of raw connection strings, one per row
   (e.g. exported from another tool with no column names at all). The
   "Group / Region for all" box is applied to every row that doesn't supply
   its own `Group` column.

New file: `Infrastructure/CsvServerImporter.cs`. No new NuGet packages are
required - the file picker uses `Microsoft.Win32.OpenFileDialog`, already
part of WPF.

Everything imported this way - whether pasted or from a CSV - is persisted
to the same local SQLite `servers.db` via `LocalServerStore.UpsertAsync`
once "Add All Parsed" is clicked, and is loaded back automatically the next
time the app starts (`MultiServerQueryViewModel.LoadAsync`, called from the
constructor).
