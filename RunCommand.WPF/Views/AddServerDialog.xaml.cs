using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using RunCommand.WPF.Infrastructure;
using RunCommand.WPF.Models;
using RunCommand.WPF.Services;
using static RunCommand.WPF.Infrastructure.SnackbarService;

namespace RunCommand.WPF.Views
{
    public partial class AddServerDialog : Window
    {
        /// <summary>Servers to add - populated from either tab. Empty list means the user cancelled.</summary>
        public System.Collections.Generic.List<ServerConnectionInfo> Results { get; } = new();

        private readonly ServerHealthChecker _healthChecker = new();
        private SnackbarMessageQueue? _restoredQueue;
        public ObservableCollection<ParsedEntry> ParsedEntries { get; } = new();

        public AddServerDialog()
        {
            InitializeComponent();
            AuthModeChanged(this, null!);
            ParsedList.ItemsSource = ParsedEntries;

            var dialogQueue = new SnackbarMessageQueue(TimeSpan.FromSeconds(4));
            DialogSnackbar.MessageQueue = dialogQueue;
            _restoredQueue = SnackbarService.UseQueue(dialogQueue);
            Closed += (_, _) => SnackbarService.RestoreQueue(_restoredQueue);
        }

        // ===================== Manual entry tab =====================

        private void AuthModeChanged(object sender, RoutedEventArgs e)
        {
            bool windowsAuth = WindowsAuthCheck.IsChecked == true;
            UserBox.Visibility = windowsAuth ? Visibility.Collapsed : Visibility.Visible;
            PassBox.Visibility = windowsAuth ? Visibility.Collapsed : Visibility.Visible;
        }

        private ServerConnectionInfo BuildFromForm() => new()
        {
            Name = NameBox.Text.Trim(),
            HostName = HostBox.Text.Trim(),
            Port = int.TryParse(PortBox.Text, out var p) ? p : 1433,
            DatabaseName = DatabaseBox.Text.Trim(),
            Group = GroupBox.Text.Trim(),
            UseWindowsAuth = WindowsAuthCheck.IsChecked == true,
            UserName = UserBox.Text.Trim(),
            EncryptedPassword = SecureStringHelper.Protect(PassBox.Password)
        };

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var candidate = BuildFromForm();

                TestDot.Fill = new SolidColorBrush(Color.FromRgb(0xE8, 0xA6, 0x1E));
                TestResultText.Text = "Testing connection...";

                await _healthChecker.CheckOneAsync(candidate, default);

                (Brush fill, string message) = candidate.Status switch
                {
                    ServerStatus.Online => (new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x4A)),
                        $"Online - {candidate.LastResponseTimeMs} ms"),
                    ServerStatus.AuthFailed => (new SolidColorBrush(Color.FromRgb(0xE8, 0x6A, 0x1E)),
                        $"Reachable but login failed: {candidate.LastError}"),
                    _ => (new SolidColorBrush(Color.FromRgb(0xD9, 0x33, 0x33)),
                        $"Down / unreachable: {candidate.LastError}")
                };

                TestDot.Fill = fill;
                TestResultText.Text = message;

                if (candidate.Status == ServerStatus.Online)
                    ShowSuccess(message);
                else
                    ShowError(message);
            }
            catch (Exception ex)
            {
                TestDot.Fill = new SolidColorBrush(Color.FromRgb(0xD9, 0x33, 0x33));
                TestResultText.Text = ex.Message;
                ShowError($"Connection test failed: {ex.Message}");
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(HostBox.Text))
            {
                ShowError("Name and Host are required.");
                return;
            }

            Results.Add(BuildFromForm());
            ShowSuccess("Server saved.");
            DialogResult = true;
        }

        // ===================== Connection-string tab =====================

        private const string PastedSource = "Pasted";

        private void ConnStringsBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // Only rebuild the rows that came from the textbox itself - leave anything
            // already imported from a CSV file untouched.
            foreach (var old in ParsedEntries.Where(x => x.Source == PastedSource).ToList())
                ParsedEntries.Remove(old);

            var lines = ConnStringsBox.Text
                .Split('\n')
                .Select(l => l.Trim().TrimEnd('\r'))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            foreach (var line in lines)
            {
                try
                {
                    var info = ConnectionStringParser.Parse(line, group: BulkGroupBox.Text.Trim());
                    ParsedEntries.Add(new ParsedEntry
                    {
                        Info = info,
                        Name = info.Name,
                        DataSource = info.HostName,
                        Database = info.DatabaseName,
                        Status = ServerStatus.Unknown,
                        Message = "Not tested yet",
                        Source = PastedSource
                    });
                }
                catch (Exception ex)
                {
                    ParsedEntries.Add(new ParsedEntry
                    {
                        Info = null,
                        Name = "(unparseable)",
                        DataSource = line.Length > 40 ? line[..40] + "..." : line,
                        Database = "",
                        Status = ServerStatus.Offline,
                        Message = $"Parse error: {ex.Message}",
                        Source = PastedSource
                    });
                }
            }

            UpdateParseSummary();
        }

        private void UpdateParseSummary()
        {
            ParseSummaryText.Text = $"{ParsedEntries.Count(x => x.Info != null)} valid, " +
                                     $"{ParsedEntries.Count(x => x.Info == null)} invalid.";
        }

        private void ImportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Servers from CSV",
                Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) != true) return;

            string content;
            try
            {
                content = System.IO.File.ReadAllText(dialog.FileName);
            }
            catch (Exception ex)
            {
                ShowError($"Could not read that file: {ex.Message}");
                return;
            }

            var defaultGroup = BulkGroupBox.Text.Trim();
            var parsed = CsvServerImporter.ParseCsv(content, string.IsNullOrWhiteSpace(defaultGroup) ? null : defaultGroup);

            if (parsed.Count == 0)
            {
                ShowError("No rows found in that CSV file.");
                return;
            }

            var sourceTag = $"CSV: {System.IO.Path.GetFileName(dialog.FileName)}";
            foreach (var p in parsed)
            {
                ParsedEntries.Add(new ParsedEntry
                {
                    Info = p.Info,
                    Name = p.Info?.Name ?? "(unparseable)",
                    DataSource = p.Info?.HostName ?? (p.RawLine.Length > 40 ? p.RawLine[..40] + "..." : p.RawLine),
                    Database = p.Info?.DatabaseName ?? "",
                    Status = p.Info != null ? ServerStatus.Unknown : ServerStatus.Offline,
                    Message = p.Info != null ? "Not tested yet" : $"Parse error: {p.Error}",
                    Source = sourceTag
                });
            }

            UpdateParseSummary();

            var validCount = parsed.Count(x => x.Info != null);
            var invalidCount = parsed.Count - validCount;
            if (invalidCount > 0)
                ShowInfo($"Imported {validCount} valid row(s). {invalidCount} row(s) could not be parsed.");
            else
                ShowSuccess($"Imported {validCount} row(s) from {System.IO.Path.GetFileName(dialog.FileName)}.");
        }

        private async void TestAll_Click(object sender, RoutedEventArgs e)
        {
            var entries = ParsedEntries.Where(x => x.Info != null).ToList();
            if (entries.Count == 0)
            {
                ShowError("No valid connection strings to test.");
                return;
            }

            try
            {
                var throttle = new SemaphoreSlim(10);
                var tasks = entries.Select(async entry =>
                {
                    await throttle.WaitAsync();
                    try
                    {
                        entry.Status = ServerStatus.Checking;
                        await _healthChecker.CheckOneAsync(entry.Info!, CancellationToken.None);
                        entry.Status = entry.Info!.Status;
                        entry.Message = entry.Info.Status switch
                        {
                            ServerStatus.Online => $"Online - {entry.Info.LastResponseTimeMs} ms",
                            ServerStatus.AuthFailed => $"Login failed: {entry.Info.LastError}",
                            _ => $"Down: {entry.Info.LastError}"
                        };
                    }
                    catch (Exception ex)
                    {
                        entry.Status = ServerStatus.Offline;
                        entry.Message = ex.Message;
                    }
                    finally
                    {
                        throttle.Release();
                    }
                });

                await Task.WhenAll(tasks);

                var online = entries.Count(e => e.Status == ServerStatus.Online);
                var failed = entries.Count - online;
                if (failed == 0)
                    ShowSuccess($"All {online} connection(s) are online.");
                else
                    ShowInfo($"Test complete: {online} online, {failed} failed.");
            }
            catch (Exception ex)
            {
                ShowError($"Bulk connection test failed: {ex.Message}");
            }
        }

        private void AddAll_Click(object sender, RoutedEventArgs e)
        {
            var valid = ParsedEntries.Where(x => x.Info != null).Select(x => x.Info!).ToList();
            if (valid.Count == 0)
            {
                ShowError("No valid connection strings to add. Paste a line or import a CSV file first.");
                return;
            }

            Results.AddRange(valid);
            ShowSuccess($"Added {valid.Count} server(s).");
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        /// <summary>Row view-model for the parsed connection-string list.</summary>
        public class ParsedEntry : INotifyPropertyChanged
        {
            public ServerConnectionInfo? Info { get; set; }
            public string Name { get; set; } = "";
            public string DataSource { get; set; } = "";
            public string Database { get; set; } = "";
            /// <summary>"Pasted" or "CSV: filename.csv" - lets the two entry sources coexist without one clearing the other.</summary>
            public string Source { get; set; } = "Pasted";

            private ServerStatus _status;
            public ServerStatus Status
            {
                get => _status;
                set { _status = value; OnPropertyChanged(); }
            }

            private string _message = "";
            public string Message
            {
                get => _message;
                set { _message = value; OnPropertyChanged(); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
