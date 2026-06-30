// ===================== PasswordPromptWindow.xaml.cs =====================
using System.Windows;

namespace GUI_Library
{
    /// <summary>
    /// Small dialog to enter a new password, which is used when admin resets a local user's password. 
    /// A PasswordBox cannot be data-bound
    /// (by design, for security), so this exposes the entered value as a plain
    /// property the caller reads after the dialog returns true.
    /// 
    /// This dialog only collects the password. The security of the password is set in LocalUserStore.cs
    /// </summary>
    public partial class ResetPasswordWindow : Window
    {
        public string NewPassword { get; private set; } = "";

        public ResetPasswordWindow(string username)
        {
            InitializeComponent();
            HeaderText.Text = "New password for \"" + username + "\"";
            PwBox.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(PwBox.Password))
            {
                Status.Text = "Enter a password.";
                return;
            }
            NewPassword = PwBox.Password;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}