using System.Windows;
using RunCommand.WPF.Views;

namespace RunCommand.WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            MainFrame.Navigate(new MultiServerQueryPage());
        }
    }
}
