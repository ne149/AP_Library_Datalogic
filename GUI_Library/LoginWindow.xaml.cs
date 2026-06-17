using System.Windows;
using System.Windows.Input;

namespace GUI_Library
{
    public partial class LoginWindow : Window
    {
        private readonly IUserService _userService;

        // Resultatet af et vellykket login - laeses af kalderen efter ShowDialog()==true.
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
                MessageBox.Show("Indtast brugernavn og kode.");
                return;
            }

            var result = _userService.Login(user, pass);
            if (result == null)
            {
                MessageBox.Show("Forkert brugernavn eller kode.");
                PasswordBox.Clear();
                PasswordBox.Focus();
                return;   // vinduet bliver staaende
            }

            AuthenticatedUser = result;
            DialogResult = true;   // kun ved succes
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