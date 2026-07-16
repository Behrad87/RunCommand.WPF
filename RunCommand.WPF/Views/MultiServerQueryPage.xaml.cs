using System.Windows.Controls;
using RunCommand.WPF.Infrastructure;
using RunCommand.WPF.ViewModels;

namespace RunCommand.WPF.Views
{
    public partial class MultiServerQueryPage : Page
    {
        private readonly MultiServerQueryViewModel _vm;

        public MultiServerQueryPage()
        {
            InitializeComponent();

            var store = new LocalServerStore(); // defaults to %AppData%\MultiServerQueryTool\servers.db
            _vm = new MultiServerQueryViewModel(store);
            _vm.OnRequestAddServer += ShowAddServerDialog;
            DataContext = _vm;
        }

        private async void ShowAddServerDialog()
        {
            var dialog = new AddServerDialog();
            if (dialog.ShowDialog() == true)
            {
                foreach (var server in dialog.Results)
                    await _vm.AddOrUpdateServerAsync(server);
            }
        }
    }
}
