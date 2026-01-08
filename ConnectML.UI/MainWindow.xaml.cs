using System.Windows;
using System.Windows.Input;

namespace ConnectML.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void StopService_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Service Stop Requested!", "Action", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
