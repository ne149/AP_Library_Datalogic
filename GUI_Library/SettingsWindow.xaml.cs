// ===================== SettingsWindow.xaml.cs =====================
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace GUI_Library
{
    /// <summary>
    /// Generic AD settings dialog
    /// The code for the AD Settings Window
    /// The admin enters server, domain, protocol/port, the
    /// LDAPS certificate policy and their credentials, fetches the list of groups
    /// in the domain, and maps each role to one of those groups. Admin credentials
    /// are used ONLY to fetch the group list and to validate - never stored.
    /// </summary>
  
    public partial class SettingsWindow : Window
    {
        private readonly AdSettings _current;

        public AdSettings SavedSettings { get; private set; }

        public SettingsWindow(AdSettings current) // Adding new active directory settings. If AD is set, set all values except username and password
        {
            InitializeComponent();

            _current = current ?? new AdSettings(); // Add new active directory settings, if no ad is set. 

            ServerBox.Text = _current.Server;
            DomainBox.Text = _current.Domain;

            ProtocolCombo.SelectedIndex = _current.UseLdaps ? 1 : 0;

            PortBox.Text = _current.Port > 0 ? _current.Port.ToString() : "";

            CertModeCombo.SelectedIndex = (int)_current.CertValidationMode;
            ThumbprintBox.Text = _current.TrustedThumbprint ?? "";

            var seeded = new List<string>(); // Preparing a drop down list of roles
            AddIfNotEmpty(seeded, _current.GroupOperator);
            AddIfNotEmpty(seeded, _current.GroupEngineer);
            AddIfNotEmpty(seeded, _current.GroupAdmin);
            if (seeded.Count > 0)
                PopulateGroupCombos(seeded);


            // Selected roles
            OperatorCombo.SelectedItem = _current.GroupOperator;
            EngineerCombo.SelectedItem = _current.GroupEngineer;
            AdminCombo.SelectedItem = _current.GroupAdmin;

            UpdateCertUiState();

            ServerBox.Focus(); // User can write in the field when the box opens
        }

        private static void AddIfNotEmpty(List<string> list, string value) // Adding roles to the dropdown menu
        {
            if (!string.IsNullOrWhiteSpace(value) && !list.Contains(value))
                list.Add(value);
        }

        private bool UseLdapsSelected => ProtocolCombo.SelectedIndex == 1; // LDAPS for 1 and LDAP for 0

        private CertValidationMode SelectedCertMode =>
            (CertValidationMode)(CertModeCombo.SelectedIndex < 0 ? 0 : CertModeCombo.SelectedIndex); // LDAPS modes - 1=Accept all, 2=Thumbprint, 3=Windows Trust

        /// <summary>
        /// Reads the port. Port is now REQUIRED.
        ///   returns  >0  : a valid port
        ///   returns   0  : the field was empty (caller must reject: required)
        ///   returns  -1  : the field had text but it wasn't a valid port number
        /// </summary>
        private int ReadPort()
        {
            string text = PortBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return 0; // empty -> "required" (handled by caller)

            int port;
            if (int.TryParse(text, out port) && port > 0 && port <= 65535)
                return port;

            return -1; // present but invalid
        }

        /// <summary>
        /// Validates the port field and produces the right error message.
        /// Returns true if the port is a usable number; otherwise false + error.
        /// </summary>
        private bool TryGetPort(out int port, out string error)
        {
            error = null;
            port = ReadPort();

            if (port == 0)
            {
                error = "Port is required.";
                return false;
            }
            if (port == -1)
            {
                error = "Port must be a number between 1 and 65535.";
                return false;
            }
            return true;
        }


        // Certification coding
        // The coding for the different certification modes is written her. 
        // Graying out unused fields, e.g. Thumbprint field on LDAPS mode 3 with Windows Trust. 
        private void Protocol_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCertUiState();
        }

        private void CertMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCertUiState();
        }

        private void UpdateCertUiState()
        {
            if (CertPanel == null || ThumbprintBox == null || CertHint == null) return;

            bool ldaps = UseLdapsSelected;
            CertPanel.IsEnabled = ldaps;
            CertPanel.Opacity = ldaps ? 1.0 : 0.4;

            bool pinning = ldaps && SelectedCertMode == CertValidationMode.TrustedCert;
            ThumbprintBox.IsEnabled = pinning;

            if (!pinning)
                ThumbprintBox.Text = "";

            if (!ldaps)
            {
                CertHint.Text = "Certificate validation applies to LDAPS only.";
            }
            else
            {
                switch (SelectedCertMode)
                {
                    case CertValidationMode.AcceptAll:
                        CertHint.Text = "Encrypts the connection but does not verify the " +
                                        "server's identity. Use only on a trusted/closed network.";
                        break;
                    case CertValidationMode.TrustedCert:
                        CertHint.Text = "Enter the server certificate's thumbprint. " +
                                        "Only that exact certificate will be accepted.";
                        break;
                    case CertValidationMode.SystemTrust:
                        CertHint.Text = "Windows validates the certificate against this " +
                                        "machine's trust store (requires a trusted CA).";
                        break;
                }
            }
        }

        /// <summary>
        /// Enables/disables the input controls while a fetch is running, and
        /// shows/hides the spinner.
        /// </summary>
        private void SetBusy(bool busy)
        {
            FetchSpinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            FetchButton.IsEnabled = !busy;
            SaveButton.IsEnabled = !busy;
        }

        private async void Fetch_Click(object sender, RoutedEventArgs e)
        {
            string server = ServerBox.Text?.Trim();
            string domain = DomainBox.Text?.Trim();
            string adminUser = AdminUserBox.Text?.Trim();
            string adminPass = AdminPasswordBox.Password;
            bool useLdaps = UseLdapsSelected;
            CertValidationMode certMode = SelectedCertMode;
            string thumbprint = ThumbprintBox.Text?.Trim();

            string portError;
            int port;
            if (!TryGetPort(out port, out portError))
            {
                FetchStatus.Text = portError;
                return;
            }

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(domain) ||
                string.IsNullOrWhiteSpace(adminUser) ||
                string.IsNullOrWhiteSpace(adminPass))
            {
                FetchStatus.Text = "Enter server, domain, admin username and password first.";
                return;
            }

            SetBusy(true);
            FetchStatus.Text = "Connecting...";

            try
            {
                // ACCESS CHECK: once configured, only a member of the saved admin
                // group may fetch. On first-time setup this is skipped.
                if (_current.IsConfigured)
                {
                    string accessError = null;
                    bool isAdmin = await Task.Run(() =>
                        AdReader.ValidateAdminConfig(
                            server, domain, port, useLdaps,
                            certMode, thumbprint,
                            adminUser, adminPass, _current.GroupAdmin, out accessError));

                    if (!isAdmin)
                    {
                        FetchStatus.Text = "Access denied: " + accessError;
                        return;
                    }
                }

                string error = null;
                List<string> groups = await Task.Run(() =>
                    AdReader.GetAllGroups(
                        server, domain, port, useLdaps,
                        certMode, thumbprint,
                        adminUser, adminPass, out error));

                if (error != null)
                {
                    FetchStatus.Text = error;
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
            finally
            {
                SetBusy(false);
            }
        }

        // Fills all three role dropdowns (Operator, Engineer, Admin) with the same
        // list of group names. 
        private void PopulateGroupCombos(List<string> groups)
        {
            OperatorCombo.ItemsSource = new List<string>(groups);
            EngineerCombo.ItemsSource = new List<string>(groups);
            AdminCombo.ItemsSource = new List<string>(groups);
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            string server = ServerBox.Text?.Trim();
            string domain = DomainBox.Text?.Trim();
            string adminUser = AdminUserBox.Text?.Trim();
            string adminPass = AdminPasswordBox.Password;
            string groupAdmin = AdminCombo.SelectedItem as string;
            bool useLdaps = UseLdapsSelected;
            CertValidationMode certMode = SelectedCertMode;
            string thumbprint = ThumbprintBox.Text?.Trim();

            string portError;
            int port;
            if (!TryGetPort(out port, out portError))
            {
                MessageBox.Show(portError);
                return;
            }

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

            SetBusy(true);
            string error = null;
            bool ok;
            try
            {
                ok = await Task.Run(() =>
                    AdReader.ValidateAdminConfig(
                        server, domain, port, useLdaps,
                        certMode, thumbprint,
                        adminUser, adminPass, groupAdmin, out error));
            }
            finally
            {
                SetBusy(false);
            }

            if (!ok)
            {
                MessageBox.Show("The settings could not be verified:\n\n" + error);
                return;
            }

            var settings = new AdSettings // Storing in a new ad settings object
            {
                Server = server,
                Domain = domain,
                Port = port,
                UseLdaps = useLdaps,
                CertValidationMode = certMode,
                TrustedThumbprint = useLdaps && certMode == CertValidationMode.TrustedCert
                                    ? thumbprint : "",
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