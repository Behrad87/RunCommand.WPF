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

            // Must assign the queue to the Snackbar control - otherwise messages enqueue to a disconnected queue.
            AppSnackbar.MessageQueue ??= new SnackbarMessageQueue(TimeSpan.FromSeconds(4));
            SnackbarService.Initialize(AppSnackbar.MessageQueue);

            MainFrame.Navigate(new MultiServerQueryPage());
        }
    }
}
