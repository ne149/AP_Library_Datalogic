using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace GUI_Library
{

    // This is the code for the Login window
    // It logs in using IUserService. 

    public partial class LoginWindow : Window
    {
        private readonly IUserService _userService;
        private bool _busy;

        // The result of a successful login - read by the caller after ShowDialog()==true.
        public AuthenticatedUser AuthenticatedUser { get; private set; }

        public LoginWindow(IUserService userService)
        {
            InitializeComponent();
            _userService = userService;
            UsernameBox.Focus();
        }

        private async void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;

            var user = UsernameBox.Text?.Trim();
            var pass = PasswordBox.Password;

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                MessageBox.Show("Enter username and password.");
                return;
            }

            SetBusy(true);

            AuthenticatedUser result;
            try
            {
                // Run the login off the UI thread. The AD bind can block for several
                // seconds when the server is unreachable (it waits for a TCP timeout),
                // and we want the spinner to keep animating instead of freezing.
                result = await Task.Run(() => _userService.Login(user, pass));
            }
            catch (Exception ex)
            {
                SetBusy(false);
                MessageBox.Show("Login failed: " + ex.Message);
                return;
            }

            SetBusy(false);

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

        private void SetBusy(bool busy) // Thinking overlay - Unavailable to click OK/Cancel etc - Unavailable to write on text fields
        {
            _busy = busy;
            BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            OkButton.IsEnabled = !busy;
            CancelButton.IsEnabled = !busy;
            UsernameBox.IsEnabled = !busy;
            PasswordBox.IsEnabled = !busy;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            DialogResult = false;
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e) // Making it possible to press 'Enter' on keyboard
        {
            if (e.Key == Key.Enter) Ok_Click(sender, e);
        }
    }
}