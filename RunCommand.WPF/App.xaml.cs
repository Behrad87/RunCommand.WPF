using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using RunCommand.WPF.Infrastructure;

namespace RunCommand.WPF
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ReportException(e.Exception, "An unexpected error occurred");
            e.Handled = true;
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
                ReportException(ex, "A fatal error occurred");
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            ReportException(e.Exception, "A background task failed");
            e.SetObserved();
        }

        private static void ReportException(Exception ex, string context)
        {
            var message = ex is AggregateException agg && agg.InnerException is not null
                ? $"{context}: {agg.InnerException.Message}"
                : $"{context}: {ex.Message}";

            SnackbarService.ShowError(message);
        }
    }
}
