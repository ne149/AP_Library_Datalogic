// ===================== LocalUsersWindow.xaml.cs =====================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GUI_Library
{
    /// <summary>
    /// Manage local (fallback) users: enable/disable, create, change role,
    /// reset password, delete.
    ///
    /// Edits a CLONE of the store. Nothing touches the live store until SAVE, so
    /// CANCEL discards everything. On SAVE the edited clone is exposed via
    /// SavedStore for the caller to apply to the session and persist.
    /// </summary>
    public partial class LocalUsersWindow : Window // The code for Local Users Window. 
    {
        private readonly LocalUserStore _clone;

        /// <summary>The edited store, valid after the dialog returns true.</summary>
        public LocalUserStore SavedStore { get; private set; }

        /// <summary>All roles, for the dropdowns. Built from the enum so adding a
        /// role later (e.g. going to 5 roles) shows up here automatically.</summary>
        public IEnumerable<UserRole> AllRoles { get; } =
            Enum.GetValues(typeof(UserRole)).Cast<UserRole>().ToList();

        public LocalUsersWindow(LocalUserStore store)
        {
            InitializeComponent();
            DataContext = this;

            // Work on a copy so Cancel is a true discard.
            _clone = (store ?? new LocalUserStore()).Clone();

            EnabledToggle.IsChecked = _clone.Enabled;
            NewRoleCombo.ItemsSource = AllRoles;
            NewRoleCombo.SelectedItem = UserRole.Operator;

            RefreshList();
        }

        private void RefreshList()
        {
            // Reset the binding so newly added/removed rows show up.
            UsersList.ItemsSource = null;
            UsersList.ItemsSource = _clone.Users;
        }

        private void Add_Click(object sender, RoutedEventArgs e) // Code for add button - Creating a local user
        {
            string username = (NewUserBox.Text ?? "").Trim();
            string password = NewPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(username))
            {
                AddStatus.Text = "Enter a username.";
                return;
            }
            if (string.IsNullOrEmpty(password))
            {
                AddStatus.Text = "Enter a password.";
                return;
            }
            if (_clone.Exists(username))
            {
                AddStatus.Text = "A user with that name already exists.";
                return;
            }

            var role = NewRoleCombo.SelectedItem is UserRole r ? r : UserRole.Operator;

            if (!_clone.AddUser(username, password, role))
            {
                AddStatus.Text = "Could not add the user.";
                return;
            }

            NewUserBox.Text = "";
            NewPasswordBox.Password = "";
            NewRoleCombo.SelectedItem = UserRole.Operator;
            AddStatus.Text = "User \"" + username + "\" added. Remember to press SAVE.";
            RefreshList();
        }

        private void ResetPassword_Click(object sender, RoutedEventArgs e)
        {
            string username = (sender as Button)?.Tag as string;
            if (string.IsNullOrWhiteSpace(username)) return;

            var dlg = new ResetPasswordWindow(username) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                if (_clone.SetPassword(username, dlg.NewPassword))
                    AddStatus.Text = "Password for \"" + username + "\" reset. Remember to press SAVE.";
                else
                    AddStatus.Text = "Could not reset the password.";
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            string username = (sender as Button)?.Tag as string;
            if (string.IsNullOrWhiteSpace(username)) return;

            var confirm = MessageBox.Show(
                "Delete user \"" + username + "\"?",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            if (!_clone.RemoveUser(username))
            {
                // RemoveUser refuses to delete the last remaining Admin.
                MessageBox.Show(
                    "Could not delete this user. You cannot delete the last " +
                    "remaining Admin user.",
                    "Not allowed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AddStatus.Text = "User \"" + username + "\" removed. Remember to press SAVE.";
            RefreshList();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _clone.Enabled = EnabledToggle.IsChecked == true;

            // Per-row role dropdowns wrote straight into each LocalUser via the
            // two-way binding, so the clone is already up to date.
            SavedStore = _clone;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}