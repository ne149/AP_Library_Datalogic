// ===================== SettingsWindow.xaml.cs =====================
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace GUI_Library
{
    /// <summary>
    /// Generic AD settings dialog (lives in GUI_Library so it can be reused by any
    /// customer project). The admin enters server, domain, protocol/port and their
    /// credentials, fetches the list of groups in the domain, and maps each role to
    /// one of those groups. Admin credentials are used ONLY to fetch the group list
    /// and to validate the configuration - they are never stored.
    ///
    /// ACCESS RULES:
    ///  - First-time setup (not configured yet): Fetch and Save are open.
    ///  - After configuration: both require credentials belonging to a member of
    ///    the SAVED admin group.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly AdSettings _current;

        public AdSettings SavedSettings { get; private set; }

        public SettingsWindow(AdSettings current)
        {
            InitializeComponent();

            _current = current ?? new AdSettings();

            ServerBox.Text = _current.Server;
            DomainBox.Text = _current.Domain;

            // Protocol dropdown: index 0 = LDAP, index 1 = LDAPS.
            ProtocolCombo.SelectedIndex = _current.UseLdaps ? 1 : 0;

            // Show the explicit port only if one was saved; otherwise leave empty
            // so the default is used.
            PortBox.Text = _current.Port > 0 ? _current.Port.ToString() : "";

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

        // True if the LDAPS option is selected.
        private bool UseLdapsSelected => ProtocolCombo.SelectedIndex == 1;

        // Reads the port from the textbox. Returns 0 (= auto) when empty.
        // Returns -1 when the text is present but not a valid port number.
        private int ReadPort()
        {
            string text = PortBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            int port;
            if (int.TryParse(text, out port) && port > 0 && port <= 65535)
                return port;

            return -1;
        }

        private void Protocol_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // PortHint is null during the initial InitializeComponent pass.
            if (PortHint == null) return;

            PortHint.Text = UseLdapsSelected
                ? "Leave port empty to use the default LDAPS port (636)."
                : "Leave port empty to use the default LDAP port (389).";
        }

        private void Fetch_Click(object sender, RoutedEventArgs e)
        {
            string server = ServerBox.Text?.Trim();
            string domain = DomainBox.Text?.Trim();
            string adminUser = AdminUserBox.Text?.Trim();
            string adminPass = AdminPasswordBox.Password;
            bool useLdaps = UseLdapsSelected;

            int port = ReadPort();
            if (port == -1)
            {
                FetchStatus.Text = "Port must be a number between 1 and 65535, or left empty.";
                return;
            }
            int effectivePort = port > 0 ? port : (useLdaps ? 636 : 389);

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(domain) ||
                string.IsNullOrWhiteSpace(adminUser) ||
                string.IsNullOrWhiteSpace(adminPass))
            {
                FetchStatus.Text = "Enter server, domain, admin username and password first.";
                return;
            }

            // ACCESS CHECK: once configured, only a member of the saved admin group
            // may fetch. On first-time setup this is skipped.
            if (_current.IsConfigured)
            {
                string accessError;
                bool isAdmin = AdDirectoryService.ValidateAdminConfig(
                    server, domain, effectivePort, useLdaps,
                    adminUser, adminPass, _current.GroupAdmin, out accessError);

                if (!isAdmin)
                {
                    FetchStatus.Text = "Access denied: " + accessError;
                    return;
                }
            }

            FetchStatus.Text = "Connecting...";

            string error;
            var groups = AdDirectoryService.GetAllGroups(
                server, domain, effectivePort, useLdaps,
                adminUser, adminPass, out error);

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

            string prevOp = OperatorCombo.SelectedItem as string;
            string prevEng = EngineerCombo.SelectedItem as string;
            string prevAdmin = AdminCombo.SelectedItem as string;

            PopulateGroupCombos(groups);

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
            bool useLdaps = UseLdapsSelected;

            int port = ReadPort();
            if (port == -1)
            {
                MessageBox.Show("Port must be a number between 1 and 65535, or left empty.");
                return;
            }
            int effectivePort = port > 0 ? port : (useLdaps ? 636 : 389);

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(domain) ||
                string.IsNullOrWhiteSpace(groupAdmin))
            {
                MessageBox.Show("Server, Domain and the Admin group are required.");
                return;
            }

            if (string.IsNullOrWhiteSpace(adminUser) || string.IsNullOrWhiteSpace(adminPass))
            {
                MessageBox.Show("Enter the admin username and password. " +
                                "They are needed to verify the settings before saving " +
                                "(they are not stored).");
                return;
            }

            string error;
            bool ok = AdDirectoryService.ValidateAdminConfig(
                server, domain, effectivePort, useLdaps,
                adminUser, adminPass, groupAdmin, out error);

            if (!ok)
            {
                MessageBox.Show("The settings could not be verified:\n\n" + error);
                return;
            }

            var settings = new AdSettings
            {
                Server = server,
                Domain = domain,
                Port = port,            // 0 when empty = auto
                UseLdaps = useLdaps,
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