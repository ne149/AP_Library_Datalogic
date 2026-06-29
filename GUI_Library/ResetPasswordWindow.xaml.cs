// ===================== PasswordPromptWindow.xaml.cs =====================
using System.Windows;

namespace GUI_Library
{
    /// <summary>
    /// Tiny dialog to enter a new password. A PasswordBox cannot be data-bound
    /// (by design, for security), so this exposes the entered value as a plain
    /// property the caller reads after the dialog returns true.
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