// ===================== AppSession.cs =====================
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GUI_Library
{
    /// <summary>
    /// Application-wide session shared by all cameras and the settings tab.
    /// Holds the current AD settings, the local-user store, and the single
    /// logged-in user, so login is global (log in once, applies to every camera)
    /// instead of per-camera.
    ///
    /// Implements INotifyPropertyChanged directly (instead of using CommunityToolkit)
    /// so GUI_Library has no NuGet dependency on the MVVM toolkit.
    /// </summary>
    public class AppSession : INotifyPropertyChanged
    {
        public AppSession()
        {
            _settings = AdSettings.Load();
            _localUsers = LocalUserStore.Load();
        }

        // ---------- settings ----------
        private AdSettings _settings;
        public AdSettings Settings
        {
            get => _settings;
            set
            {
                _settings = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsConfigured));
                OnPropertyChanged(nameof(IsSettingsTabVisible));
            }
        }

        // ---------- local users (fallback authentication) ----------
        private LocalUserStore _localUsers;
        public LocalUserStore LocalUsers
        {
            get => _localUsers;
            set
            {
                _localUsers = value;
                OnPropertyChanged();
            }
        }

        public bool IsConfigured => Settings != null && Settings.IsConfigured;

        /// <summary>
        /// Builds an IUserService for the login dialog. Always returns a LoginService,
        /// which itself enforces the three states:
        ///   1. No AD configured        -> local users only.
        ///   2. AD configured, local off -> AD only.
        ///   3. AD configured, local on  -> AD first, local fallback if AD unreachable.
        ///
        /// Never returns null any more: even with no AD configured there may be local
        /// users to log in with (and if there are none either, the login simply fails,
        /// while the Settings tab stays visible so the first local user can be created).
        /// </summary>
        public IUserService CreateUserService()
        {
            return new LoginService(Settings, LocalUsers);
        }

        // ---------- logged-in user ----------
        private AuthenticatedUser _user;
        public AuthenticatedUser User
        {
            get => _user;
            set
            {
                _user = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsLoggedIn));
                OnPropertyChanged(nameof(LoginButtonText));
                OnPropertyChanged(nameof(CurrentUser));
                OnPropertyChanged(nameof(IsAdmin));
                OnPropertyChanged(nameof(IsSettingsTabVisible));
                OnPropertyChanged(nameof(CanOperate));
                OnPropertyChanged(nameof(CanEditGauge));
                OnPropertyChanged(nameof(CanEditBlob));
                OnPropertyChanged(nameof(CanSaveProgram));
                OnPropertyChanged(nameof(CanViewAudit));
                UserChanged?.Invoke();
            }
        }

        // Raised whenever the user logs in or out (cameras subscribe to refresh).
        public event Action UserChanged;

        public bool IsLoggedIn => User != null;
        public string LoginButtonText => IsLoggedIn ? "Log out" : "Log in";
        public string CurrentUser => User?.Username ?? "";

        // Admin = has the audit permission (only the admin role grants it).
        public bool IsAdmin => User?.Has(Permission.CanViewAudit) ?? false;

        // Permissions surfaced for binding.
        public bool CanOperate => User?.Has(Permission.CanOperate) ?? false;
        public bool CanEditGauge => User?.Has(Permission.CanEditGauge) ?? false;
        public bool CanEditBlob => User?.Has(Permission.CanEditBlob) ?? false;
        public bool CanSaveProgram => User?.Has(Permission.CanSaveProgram) ?? false;
        public bool CanViewAudit => User?.Has(Permission.CanViewAudit) ?? false;

        // Audit display name, "unknown" when nobody is logged in.
        public string AuditUser => User?.AuditName ?? "unknown";

        /// <summary>
        /// Settings tab visibility rule:
        ///  - Not configured yet (first run) -> visible to everyone (initial setup).
        ///  - Configured -> visible only when an Admin is logged in.
        /// </summary>
        public bool IsSettingsTabVisible => !IsConfigured || IsAdmin;

        // ---------- INotifyPropertyChanged ---------- // Changing GUI when a property is changed, eg from "log in" to "logged in"
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}