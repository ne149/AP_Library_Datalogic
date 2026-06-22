// ===================== SettingsWindow.xaml.cs =====================
using System.Collections.Generic;
using System.Windows;

namespace GUI_Library
{
    /// <summary>
    /// Generic AD settings dialog (lives in GUI_Library so it can be reused by any
    /// customer project). The admin enters server, domain and their credentials,
    /// fetches the list of groups that exist in the domain, and maps each role to
    /// one of those groups. Admin credentials are used ONLY to fetch the group
    /// list and to validate the configuration - they are never stored.
    ///
    /// ACCESS RULES:
    ///  - First-time setup (not configured yet): Fetch and Save are open, so the
    ///    installer/admin can do the initial configuration.
    ///  - After configuration: both Fetch and Save require that the entered
    ///    credentials belong to a user who is a member of the SAVED admin group.
    ///    This stops a non-admin (e.g. the 'blob' user) from listing every group
    ///    in the domain or changing the configuration.
    ///
    /// On Save the configuration is also validated against AD: the admin
    /// credentials must connect AND the admin user must be a member of the chosen
    /// admin group, which prevents saving a wrong IP/domain (lock-out) and stops
    /// you from choosing an admin group you are not in.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        // The settings the dialog was opened with (used to know if we are already
        // configured and, if so, which admin group to check access against).
        private readonly AdSettings _current;

        // The settings after a successful save (read by the caller when DialogResult == true).
        public AdSettings SavedSettings { get; private set; }

        public SettingsWindow(AdSettings current)
        {
            InitializeComponent();

            _current = current ?? new AdSettings();

            // Pre-fill the connection fields and mapping from the existing settings
            // so the admin edits rather than re-types everything.
            ServerBox.Text = _current.Server;
            DomainBox.Text = _current.Domain;

            // Seed the combo boxes with the currently-saved group names so they
            // show even before a fresh fetch.
            var seeded = new List<string>();
            AddIfNotEmpty(seeded, _current.GroupOperator);
            AddIfNotEmpty(seeded, _current.GroupEngineer);
            AddIfNotEmpty(seeded, _current.GroupAdmin);
            if (seeded.Count > 0)
                PopulateGroupCombos(seeded);

            OperatorCombo.SelectedItem = _current.GroupOperator;
            EngineerCombo.SelectedItem = _current.GroupEngineer;
            AdminCombo.SelectedItem = _current.GroupAdmin;

            ServerBox.Focus();
        }

        private static void AddIfNotEmpty(List<string> list, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !list.Contains(value))
                list.Add(value);
        }

        // Fetch all groups from AD using the supplied admin credentials.
        private void Fetch_Click(object sender, RoutedEventArgs e)
        {
            string server = ServerBox.Text?.Trim();
            string domain = DomainBox.Text?.Trim();
            string adminUser = AdminUserBox.Text?.Trim();
            string adminPass = AdminPasswordBox.Password;

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(adminUser) ||
                string.IsNullOrWhiteSpace(adminPass))
            {
                FetchStatus.Text = "Enter server, admin username and password first.";
                return;
            }

            // ACCESS CHECK: once the system is configured, only a member of the saved
            // admin group may fetch the group list. On first-time setup this is skipped
            // so the initial admin can get started.
            if (_current.IsConfigured)
            {
                string accessError;
                bool isAdmin = AdDirectoryService.ValidateAdminConfig(
                    server, domain, adminUser, adminPass, _current.GroupAdmin, out accessError);

                if (!isAdmin)
                {
                    FetchStatus.Text = "Access denied: " + accessError;
                    return;
                }
            }

            FetchStatus.Text = "Connecting...";

            string error;
            var groups = AdDirectoryService.GetAllGroups(
                server, domain, adminUser, adminPass, out error);

            if (error != null)
            {
                FetchStatus.Text = "Error: " + error;
                return;
            }

            if (groups.Count == 0)
            {
                FetchStatus.Text = "Connected, but no groups were found.";
                return;
            }

            // Remember what was selected so we can keep the selection after refresh.
            string prevOp = OperatorCombo.SelectedItem as string;
            string prevEng = EngineerCombo.SelectedItem as string;
            string prevAdmin = AdminCombo.SelectedItem as string;

            PopulateGroupCombos(groups);

            // Restore previous selections if they still exist in the new list.
            if (prevOp != null && groups.Contains(prevOp)) OperatorCombo.SelectedItem = prevOp;
            if (prevEng != null && groups.Contains(prevEng)) EngineerCombo.SelectedItem = prevEng;
            if (prevAdmin != null && groups.Contains(prevAdmin)) AdminCombo.SelectedItem = prevAdmin;

            FetchStatus.Text = "Found " + groups.Count + " group(s). Select the mapping below.";
        }

        private void PopulateGroupCombos(List<string> groups)
        {
            OperatorCombo.ItemsSource = new List<string>(groups);
            EngineerCombo.ItemsSource = new List<string>(groups);
            AdminCombo.ItemsSource = new List<string>(groups);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string server = ServerBox.Text?.Trim();
            string domain = DomainBox.Text?.Trim();
            string adminUser = AdminUserBox.Text?.Trim();
            string adminPass = AdminPasswordBox.Password;
            string groupAdmin = AdminCombo.SelectedItem as string;

            // Server, domain and an Admin group are the minimum required so that
            // someone can always log in as admin afterwards.
            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(domain) ||
                string.IsNullOrWhiteSpace(groupAdmin))
            {
                MessageBox.Show("Server, Domain and the Admin group are required.");
                return;
            }

            // Admin credentials are required to validate the configuration before saving.
            if (string.IsNullOrWhiteSpace(adminUser) || string.IsNullOrWhiteSpace(adminPass))
            {
                MessageBox.Show("Enter the admin username and password. " +
                                "They are needed to verify the settings before saving " +
                                "(they are not stored).");
                return;
            }

            // VALIDATE against AD: credentials must connect AND the admin user must be
            // a member of the chosen admin group. This blocks a wrong IP/domain and
            // stops you from locking yourself out.
            string error;
            bool ok = AdDirectoryService.ValidateAdminConfig(
                server, domain, adminUser, adminPass, groupAdmin, out error);

            if (!ok)
            {
                MessageBox.Show("The settings could not be verified:\n\n" + error);
                return;
            }

            var settings = new AdSettings
            {
                Server = server,
                Domain = domain,
                GroupOperator = OperatorCombo.SelectedItem as string ?? "",
                GroupEngineer = EngineerCombo.SelectedItem as string ?? "",
                GroupAdmin = groupAdmin
            };

            if (!settings.Save())
            {
                MessageBox.Show("Could not save the settings file. Check write permissions.");
                return;
            }

            SavedSettings = settings;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}