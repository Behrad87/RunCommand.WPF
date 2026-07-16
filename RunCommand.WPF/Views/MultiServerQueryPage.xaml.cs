using System;
using System.Windows;
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

            var store = new LocalServerStore();
            _vm = new MultiServerQueryViewModel(store);
            _vm.OnRequestAddServer += ShowAddServerDialog;
            DataContext = _vm;
        }

        private async void ShowAddServerDialog()
        {
            try
            {
                var dialog = new AddServerDialog { Owner = Window.GetWindow(this) };
                if (dialog.ShowDialog() == true)
                {
                    foreach (var server in dialog.Results)
                        await _vm.AddOrUpdateServerAsync(server);
                }
            }
            catch (Exception ex)
            {
                SnackbarService.ShowError($"Failed to add server(s): {ex.Message}");
            }
        }
    }
}
