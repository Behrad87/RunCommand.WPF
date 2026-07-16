using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using RunCommand.WPF.Infrastructure;
using RunCommand.WPF.Models;
using RunCommand.WPF.Services;

namespace RunCommand.WPF.ViewModels
{
    public class MultiServerQueryViewModel : INotifyPropertyChanged
    {
        private readonly LocalServerStore _store;
        private readonly ServerHealthChecker _healthChecker = new();
        private readonly MultiServerQueryRunner _queryRunner = new();

        private CancellationTokenSource? _cts;

        public ObservableCollection<ServerItemViewModel> Servers { get; } = new();
        public ObservableCollection<QueryExecutionResult> Results { get; } = new();
        public ICollectionView ServersView { get; }

        public MultiServerQueryViewModel(LocalServerStore store)
        {
            _store = store;
            ServersView = CollectionViewSource.GetDefaultView(Servers);
            ServersView.Filter = FilterServer;

            AddServerCommand = new RelayCommand(_ => OnRequestAddServer?.Invoke());
            RemoveSelectedCommand = new RelayCommand(async _ => await RemoveSelectedAsync(), _ => Servers.Any(s => s.IsSelected));
            CheckSelectedStatusCommand = new RelayCommand(async _ => await CheckStatusAsync(selectedOnly: true), _ => !IsBusy);
            CheckAllStatusCommand = new RelayCommand(async _ => await CheckStatusAsync(selectedOnly: false), _ => !IsBusy && Servers.Count > 0);
            RunQueryCommand = new RelayCommand(async _ => await RunQueryAsync(), _ => !IsBusy && Servers.Any(s => s.IsSelected) && !string.IsNullOrWhiteSpace(SqlText));
            CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsBusy);
            ToggleSelectAllCommand = new RelayCommand(_ => ToggleSelectAll());

            _ = LoadAsync();
        }

        // ----- bindable properties -----

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); ServersView.Refresh(); }
        }

        private string _sqlText = "";
        public string SqlText
        {
            get => _sqlText;
            set { _sqlText = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); CommandManagerRefresh(); }
        }

        private int _progressCurrent;
        public int ProgressCurrent
        {
            get => _progressCurrent;
            set { _progressCurrent = value; OnPropertyChanged(); }
        }

        private int _progressTotal;
        public int ProgressTotal
        {
            get => _progressTotal;
            set { _progressTotal = value; OnPropertyChanged(); }
        }

        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        /// <summary>Cap on simultaneous connections. Expose this as a settings field in the UI - keep well under total server count.</summary>
        public int MaxConcurrency { get; set; } = 40;

        // ----- commands -----

        public RelayCommand AddServerCommand { get; }
        public RelayCommand RemoveSelectedCommand { get; }
        public RelayCommand CheckSelectedStatusCommand { get; }
        public RelayCommand CheckAllStatusCommand { get; }
        public RelayCommand RunQueryCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand ToggleSelectAllCommand { get; }

        /// <summary>View subscribes to this and shows the Add Server dialog.</summary>
        public event Action? OnRequestAddServer;

        // ----- loading / persistence -----

        private async Task LoadAsync()
        {
            var all = await _store.GetAllAsync();
            foreach (var s in all)
                Servers.Add(new ServerItemViewModel(s));
            StatusMessage = $"Loaded {Servers.Count} servers.";
        }

        public async Task AddOrUpdateServerAsync(ServerConnectionInfo info)
        {
            await _store.UpsertAsync(info);
            var existing = Servers.FirstOrDefault(s => s.Info.Id == info.Id);
            if (existing == null)
                Servers.Add(new ServerItemViewModel(info));
            else
                existing.RefreshFromModel();
        }

        private async Task RemoveSelectedAsync()
        {
            var toRemove = Servers.Where(s => s.IsSelected).ToList();
            foreach (var s in toRemove)
            {
                await _store.DeleteAsync(s.Info.Id);
                Servers.Remove(s);
            }
            StatusMessage = $"Removed {toRemove.Count} server(s).";
        }

        private void ToggleSelectAll()
        {
            // Applies to whatever is currently visible in the filtered view - important
            // once you're filtering thousands of rows down to one region/group.
            var visible = ServersView.Cast<ServerItemViewModel>().ToList();
            bool makeSelected = visible.Any(s => !s.IsSelected);
            foreach (var s in visible) s.IsSelected = makeSelected;
        }

        private bool FilterServer(object obj)
        {
            if (string.IsNullOrWhiteSpace(SearchText)) return true;
            if (obj is not ServerItemViewModel s) return false;
            var q = SearchText.Trim();
            return s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.HostName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.Group.Contains(q, StringComparison.OrdinalIgnoreCase)
                || s.DatabaseName.Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        // ----- status checks -----

        private async Task CheckStatusAsync(bool selectedOnly)
        {
            var targets = (selectedOnly ? Servers.Where(s => s.IsSelected) : Servers).ToList();
            if (targets.Count == 0) return;

            _cts = new CancellationTokenSource();
            IsBusy = true;
            ProgressCurrent = 0;
            ProgressTotal = targets.Count;
            StatusMessage = $"Checking status of {targets.Count} server(s)...";

            var progress = new Progress<ServerConnectionInfo>(_ =>
            {
                // Fires on the UI thread (SynchronizationContext captured by Progress<T>).
                ProgressCurrent++;
            });

            try
            {
                await _healthChecker.CheckAllAsync(
                    targets.Select(t => t.Info), MaxConcurrency, progress, _cts.Token);
            }
            catch (OperationCanceledException) { /* user cancelled */ }
            finally
            {
                foreach (var t in targets) t.RefreshFromModel();
                await _store.UpdateStatusBatchAsync(targets.Select(t => t.Info));

                var online = targets.Count(t => t.Status == ServerStatus.Online);
                var offline = targets.Count(t => t.Status == ServerStatus.Offline);
                StatusMessage = $"Status check complete: {online} online, {offline} offline.";
                IsBusy = false;
            }
        }

        // ----- query execution -----

        private async Task RunQueryAsync()
        {
            var targets = Servers.Where(s => s.IsSelected).ToList();
            if (targets.Count == 0 || string.IsNullOrWhiteSpace(SqlText)) return;

            _cts = new CancellationTokenSource();
            IsBusy = true;
            ProgressCurrent = 0;
            ProgressTotal = targets.Count;
            Results.Clear();
            StatusMessage = $"Running query against {targets.Count} server(s)...";

            // Only capture full result sets when the selection is small enough that
            // holding every DataTable in memory is safe.
            bool captureFullData = targets.Count <= 50;

            var progress = new Progress<QueryExecutionResult>(r =>
            {
                Results.Add(r);
                ProgressCurrent++;
            });

            try
            {
                await _queryRunner.ExecuteAsync(
                    targets.Select(t => t.Info),
                    SqlText,
                    MaxConcurrency,
                    commandTimeoutSeconds: 30,
                    captureFullResultSet: captureFullData,
                    progress,
                    _cts.Token);
            }
            catch (OperationCanceledException) { /* user cancelled */ }
            finally
            {
                var ok = Results.Count(r => r.Success);
                var failed = Results.Count(r => !r.Success);
                StatusMessage = $"Query finished: {ok} succeeded, {failed} failed.";
                IsBusy = false;
            }
        }

        private void CommandManagerRefresh()
        {
            AddServerCommand.RaiseCanExecuteChanged();
            RemoveSelectedCommand.RaiseCanExecuteChanged();
            CheckSelectedStatusCommand.RaiseCanExecuteChanged();
            CheckAllStatusCommand.RaiseCanExecuteChanged();
            RunQueryCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
