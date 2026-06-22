// ===================== AppSession.cs =====================
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GUI_Library
{
    /// <summary>
    /// Application-wide session shared by all cameras and the settings tab.
    /// Holds the current AD settings and the single logged-in user, so login is
    /// global (log in once, applies to every camera) instead of per-camera.
    ///
    /// This lives in GUI_Library because it is generic: it knows about settings,
    /// a user and permissions, but nothing customer-specific.
    ///
    /// Implements INotifyPropertyChanged directly (instead of using CommunityToolkit)
    /// so GUI_Library has no NuGet dependency on the MVVM toolkit.
    /// </summary>
    public class AppSession : INotifyPropertyChanged
    {
        public AppSession()
        {
            _settings = AdSettings.Load();
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

        public bool IsConfigured => Settings != null && Settings.IsConfigured;

        /// <summary>
        /// Builds an IUserService from the current settings. Returns null if the
        /// app is not configured yet (no AD details entered).
        /// </summary>
        public IUserService CreateUserService()
        {
            if (!IsConfigured) return null;

            return new AdUserService(
                Settings.Server,
                Settings.Domain,
                Settings.GroupOperator,
                Settings.GroupEngineer,
                Settings.GroupAdmin);
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

        // ---------- INotifyPropertyChanged ----------
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}