using System.Windows;
using System.Windows.Input;

namespace GUI_Library
{
    public partial class LoginWindow : Window
    {
        private readonly IUserService _userService;

        // The result of a successful login - read by the caller after ShowDialog()==true.
        public AuthenticatedUser AuthenticatedUser { get; private set; }

        public LoginWindow(IUserService userService)
        {
            InitializeComponent();
            _userService = userService;
            UsernameBox.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var user = UsernameBox.Text?.Trim();
            var pass = PasswordBox.Password;

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                MessageBox.Show("Enter username and password.");
                return;
            }

            var result = _userService.Login(user, pass);
            if (result == null)
            {
                MessageBox.Show("Incorrect username or password.");
                PasswordBox.Clear();
                PasswordBox.Focus();
                return;   // the window stays open
            }

            AuthenticatedUser = result;
            DialogResult = true;   // only on success
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Ok_Click(sender, e);
        }
    }
}