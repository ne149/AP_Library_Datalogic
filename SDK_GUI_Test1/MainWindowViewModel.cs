// ===================== MainWindowViewModel.cs =====================
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GUI_Library;

namespace SDK_GUI_Test1
{
    /// <summary>
    /// Holds all cameras plus the shared AppSession (settings + global login).
    /// The start page shows an overview; each camera also has its own tab; and a
    /// Settings tab (Admin-only, except on first run) lets the customer configure AD
    /// and manage local fallback users.
    /// </summary>
    public class MainWindowViewModel : ObservableObject
    {
        // Shared session: one settings object and one logged-in user for the whole app.
        public AppSession Session { get; }

        public ObservableCollection<CameraViewModel> Cameras { get; }

        // Opens the AD settings dialog.
        public RelayCommand OpenSettingsCommand { get; }

        // Opens the local user management dialog.
        public RelayCommand OpenUsersCommand { get; }

        public MainWindowViewModel()
        {
            Session = new AppSession();

            Cameras = new ObservableCollection<CameraViewModel>
            {
                // Active camera (the one we are working on now).
                new CameraViewModel(Session, "Label Inspection : Camera 1", "192.168.1.128", 10001, active: true),
                // Empty slots - added when the real cameras are connected.
                new CameraViewModel(Session, "Camera 2", "192.168.1.129", 10010, active: false),
                new CameraViewModel(Session, "Camera 3", "192.168.1.130", 10010, active: false),
                new CameraViewModel(Session, "Camera 4", "192.168.1.131", 10010, active: false),
                new CameraViewModel(Session, "Camera 5", "192.168.1.132", 10010, active: false),
            };

            OpenSettingsCommand = new RelayCommand(OpenSettings);
            OpenUsersCommand = new RelayCommand(OpenUsers);
        }

        private void OpenSettings()
        {
            // Guard: once configured, only an Admin may change settings.
            if (Session.IsConfigured && !Session.IsAdmin)
            {
                MessageBox.Show("Only an administrator can change the AD settings.");
                return;
            }

            var dlg = new SettingsWindow(Session.Settings)
            {
                Owner = Application.Current?.MainWindow
            };

            if (dlg.ShowDialog() == true)
            {
                // Apply the new settings to the shared session immediately.
                Session.Settings = dlg.SavedSettings;
                AuditLogger.Log(Session.AuditUser, "Settings", "AD settings updated",
                                detail: dlg.SavedSettings.Server + " / " + dlg.SavedSettings.Domain);
                MessageBox.Show("Settings saved.");
            }
        }

        private void OpenUsers()
        {
            // Same guard as AD: once AD is configured, only an Admin may manage users.
            // (On first run, with no AD configured, anyone can create the first local user.)
            if (Session.IsConfigured && !Session.IsAdmin)
            {
                MessageBox.Show("Only an administrator can manage local users.");
                return;
            }

            var dlg = new LocalUsersWindow(Session.LocalUsers)
            {
                Owner = Application.Current?.MainWindow
            };

            if (dlg.ShowDialog() == true)
            {
                Session.LocalUsers = dlg.SavedStore;
                if (!dlg.SavedStore.Save())
                {
                    MessageBox.Show("Users could not be saved to disk.");
                }
                else
                {
                    AuditLogger.Log(Session.AuditUser, "Settings", "Local users updated",
                                    detail: "count=" + dlg.SavedStore.Users.Count +
                                            ", enabled=" + dlg.SavedStore.Enabled);
                    MessageBox.Show("Local users saved.");
                }
            }
        }
    }
}