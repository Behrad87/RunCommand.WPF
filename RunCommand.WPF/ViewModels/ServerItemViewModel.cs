using System.ComponentModel;
using System.Runtime.CompilerServices;
using RunCommand.WPF.Models;

namespace RunCommand.WPF.ViewModels
{
    public class ServerItemViewModel : INotifyPropertyChanged
    {
        public ServerConnectionInfo Info { get; }

        public ServerItemViewModel(ServerConnectionInfo info) => Info = info;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string Name => Info.Name;
        public string HostName => Info.HostName;
        public string DatabaseName => Info.DatabaseName;
        public string Group => Info.Group;

        public ServerStatus Status
        {
            get => Info.Status;
            set { Info.Status = value; OnPropertyChanged(); OnPropertyChanged(nameof(LastCheckedDisplay)); }
        }

        public string LastCheckedDisplay =>
            Info.LastCheckedUtc.HasValue
                ? Info.LastCheckedUtc.Value.ToLocalTime().ToString("HH:mm:ss")
                : "never";

        public string ResponseTimeDisplay =>
            Info.LastResponseTimeMs.HasValue ? $"{Info.LastResponseTimeMs} ms" : "-";

        public string? LastError => Info.LastError;

        public void RefreshFromModel()
        {
            // Call after a background health-check / query mutates Info directly,
            // so the bound UI properties fire change notifications.
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(LastCheckedDisplay));
            OnPropertyChanged(nameof(ResponseTimeDisplay));
            OnPropertyChanged(nameof(LastError));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
