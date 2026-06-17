using System.Windows;

namespace SDK_GUI_Test1
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
        }

        private void CameraPanel_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}