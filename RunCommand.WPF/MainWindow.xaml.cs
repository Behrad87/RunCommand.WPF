using System;
using System.Windows;
using MaterialDesignThemes.Wpf;
using RunCommand.WPF.Infrastructure;
using RunCommand.WPF.Views;

namespace RunCommand.WPF
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            SnackbarService.Initialize(AppSnackbar.MessageQueue ?? new SnackbarMessageQueue(TimeSpan.FromSeconds(4)));
            MainFrame.Navigate(new MultiServerQueryPage());
        }
    }
}
