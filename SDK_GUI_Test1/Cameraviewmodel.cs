// Test123


using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Windows;
using System.Windows.Threading;
using VisionSDK;
using VisionSDK.AddOn.Base;
using VisionSDK.AddOn.Base.Controllers;
using VisionSDK.AddOn.Base.Models;
using VisionSDK.AddOn.Wpf.Common;
using GUI_Library;
namespace SDK_GUI_Test1
{
    /// <summary>
    /// ONE camera. Its own VisionDeviceWrapper (one connection).
    /// AddOn controls (PropertyTolerance, PropertyBlock, ImageViewer) bind via endpoints.
    ///
    /// Logging of parameter changes happens ONLY on trigger (DoTrigger): we compare
    /// the current gauge and blob values with the ones from the last trigger and log
    /// the difference with the user that is logged in. Adjustments back and forth
    /// WITHOUT a trigger are not logged - only the value that is actually used for an
    /// inspection has audit value.
    ///
    /// Access is controlled by permissions (Permission) from the logged-in user:
    ///   CanOperate     -> online/offline/trigger
    ///   CanEditGauge   -> gauge tolerance (XAML: IsEnabled="{Binding CanEditGauge}")
    ///   CanEditBlob    -> blob min/max    (XAML: IsEnabled="{Binding CanEditBlob}")
    ///   CanSaveProgram -> save program
    ///   CanViewAudit   -> view audit log (future display)
    /// </summary>
    public class CameraViewModel : ObservableObject, IReprocess
    {
        private readonly VisionDeviceManager _manager = new VisionDeviceManager();
        private readonly string _ip;
        private readonly int _port;
        private readonly DispatcherTimer _statusTimer;

        public CameraViewModel(string title, string ip, int port, bool active = true)
        {
            Title = title;
            _ip = ip;
            _port = port;
            IsActive = active;

            if (!IsActive)
                return;

            try
            {
                var info = new VisionDeviceInfo { IpAddress = _ip, SdkPort = _port };
                Device = _manager.GetVisionDevice(info);
                Device.Connect();

                // Online so PropertyTolerance can write changes to the camera.
                Device.VisionDevice?.SetOnline_Sync(true);

                // Auto-reprocess: when an AddOn control changes -> Reprocess() is called
                // -> the change is written and the controls reload. WITHOUT this,
                // PropertyTolerance editing does not work.
                AutoReprocess.ReprocessInstance = this;

                ExternalTrigger = !ExternalTrigger;

                // Baseline on connect (starting point for the first trigger comparison).
                _lastGauge = ReadGauge(Device.VisionDevice);
                _lastBlob = ReadBlob(Device.VisionDevice);
            }
            catch { /* shown as "Not connected" in the status */ }

            LoginCommand = new RelayCommand(DoLogin);
            SaveProgramCommand = new RelayCommand(DoSaveProgram);
            TriggerCommand = new RelayCommand(DoTrigger);
            OnlineCommand = new RelayCommand(() => SetOnline(true));
            OfflineCommand = new RelayCommand(() => SetOnline(false));

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += (s, e) => RefreshStatus();
            _statusTimer.Start();

            RefreshStatus();
        }

        // ===================== IDENTITY =====================
        public string Title { get; }
        public bool IsActive { get; }

        private VisionDeviceWrapper _device;
        public VisionDeviceWrapper Device
        {
            get => _device;
            set => SetProperty(ref _device, value);
        }

        private bool _externalTrigger;
        public bool ExternalTrigger
        {
            get => _externalTrigger;
            set => SetProperty(ref _externalTrigger, value);
        }

        // ===================== IReprocess =====================
        // Called by the AddOn framework when a control value is changed by the user.
        // We re-evaluate the CURRENT image (do NOT take a new one) so pass/fail updates.
        // NO logging here - an adjustment without a trigger has no audit value.
        public void Reprocess()
        {
            try
            {
                var dev = Device?.VisionDevice;
                if (dev == null) return;

                dev.CallRunOnTask_Sync(VisionPort.CreateFromPath(VisionPorts.IMAGE_IN_TASK));
                ExternalTrigger = !ExternalTrigger;
                RefreshGauge(dev);
            }
            catch { }
        }

        // ===================== PARAMETER SNAPSHOT (for trigger logging) =====================
        private class TolSnapshot
        {
            public double Nominal;
            public double Minus;   // for range: Start
            public double Plus;    // for range: End
            public bool Valid;
        }

        private TolSnapshot _lastGauge;
        private TolSnapshot _lastBlob;

        // Gauge: comes back as VisionTolerance (Nominal/Minus/Plus) - confirmed.
        private TolSnapshot ReadGauge(dynamic dev)
        {
            if (dev == null) return null;
            try
            {
                var tol = ReadValue<VisionTolerance>(
                    dev.GetTolerancePortValue_Sync(
                        VisionPort.CreateFromPath(VisionPorts.GAUGE_TOLERANCE)));
                if (tol == null) return null;
                return new TolSnapshot
                {
                    Nominal = tol.Nominal,
                    Minus = tol.MinusTolerance,
                    Plus = tol.PlusTolerance,
                    Valid = true
                };
            }
            catch { return null; }
        }

        // Blob "Required Number Of Blobs" is a Range 1D - read with GetRange1DPortValue_Sync
        // (NOT GetTolerancePortValue_Sync). The field names are StartValue/EndValue (confirmed),
        // but we read defensively via reflection for robustness.
        private TolSnapshot ReadBlob(dynamic dev)
        {
            if (dev == null) return null;
            try
            {
                object range = dev.GetRange1DPortValue_Sync(
                    VisionPort.CreateFromPath(VisionPorts.BLOB_REQUIRED))?.ReturnValue;
                if (range == null) return null;

                double? lo = GetFirstDoubleProp(range, "StartValue", "Start", "Min", "Minimum", "Low");
                double? hi = GetFirstDoubleProp(range, "EndValue", "End", "Max", "Maximum", "High");
                if (lo == null && hi == null) return null;

                return new TolSnapshot
                {
                    Minus = lo ?? 0,
                    Plus = hi ?? 0,
                    Nominal = ((lo ?? 0) + (hi ?? 0)) / 2.0,
                    Valid = true
                };
            }
            catch { return null; }
        }

        // Gets the first property found among the given names, as a double.
        private double? GetFirstDoubleProp(object obj, params string[] names)
        {
            if (obj == null) return null;
            var type = obj.GetType();
            foreach (var name in names)
            {
                var p = type.GetProperty(name);
                if (p != null)
                {
                    try { return Convert.ToDouble(p.GetValue(obj)); } catch { }
                }
            }
            return null;
        }

        // Compares two snapshots field by field and logs only what changed.
        private void LogDiff(string tool, TolSnapshot oldT, TolSnapshot newT)
        {
            if (oldT == null || newT == null || !oldT.Valid || !newT.Valid) return;

            // Only the gauge has a real Nominal. Blob is a pure min/max range.
            if (tool == "Gauge" && Math.Abs(oldT.Nominal - newT.Nominal) > 0.0001)
                AuditLogger.Log(AuditUser, "Parameter", tool + " Nominal", Title,
                                oldValue: oldT.Nominal.ToString("0.##"),
                                newValue: newT.Nominal.ToString("0.##"));

            if (Math.Abs(oldT.Minus - newT.Minus) > 0.0001)
                AuditLogger.Log(AuditUser, "Parameter", tool + " Min", Title,
                                oldValue: oldT.Minus.ToString("0.##"),
                                newValue: newT.Minus.ToString("0.##"));

            if (Math.Abs(oldT.Plus - newT.Plus) > 0.0001)
                AuditLogger.Log(AuditUser, "Parameter", tool + " Max", Title,
                                oldValue: oldT.Plus.ToString("0.##"),
                                newValue: newT.Plus.ToString("0.##"));
        }

        // ===================== STATUS =====================
        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set { SetProperty(ref _isConnected, value); OnPropertyChanged(nameof(ConnectionText)); }
        }

        private bool _isOnline;
        public bool IsOnline
        {
            get => _isOnline;
            set { SetProperty(ref _isOnline, value); OnPropertyChanged(nameof(OnlineText)); }
        }
        public string ConnectionText => IsConnected ? "Connected" : "Not connected";
        public string OnlineText => IsOnline ? "Online" : "Offline";

        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set => SetProperty(ref _hasUnsavedChanges, value);
        }

        // ===================== GAUGE RESULT =====================
        private string _measuredValue = "N/A";
        public string MeasuredValue
        {
            get => _measuredValue;
            set => SetProperty(ref _measuredValue, value);
        }

        private bool _gaugePass;
        public bool GaugePass
        {
            get => _gaugePass;
            set { SetProperty(ref _gaugePass, value); OnPropertyChanged(nameof(GaugePassText)); }
        }
        public string GaugePassText => GaugePass ? "PASS" : "FAIL";

        private bool _blobPass;
        public bool BlobPass
        {
            get => _blobPass;
            set { SetProperty(ref _blobPass, value); OnPropertyChanged(nameof(BlobPassText)); }
        }
        public string BlobPassText => BlobPass ? "PASS" : "FAIL";

        private bool ReadSuccess(dynamic op)
        {
            if (op == null) return false;
            try { return (bool)op.IsSuccess; } catch { return false; }
        }

        private T ReadValue<T>(dynamic op)
        {
            if (op == null) return default(T);
            try { return (T)op.ReturnValue; } catch { return default(T); }
        }

        private void RefreshStatus()
        {
            try
            {
                var dev = Device?.VisionDevice;
                IsConnected = dev != null && dev.IsConnected;
                IsOnline = IsConnected && ReadValue<bool>(dev.GetOnlineState_Sync());

                if (IsConnected)
                    RefreshGauge(dev);
            }
            catch { IsConnected = false; IsOnline = false; }

            if (AuditLogger.HasPending)
                AuditLogger.MergePending();
        }

        private void RefreshGauge(dynamic dev)
        {
            if (dev == null) return;
            try
            {
                var passOp = dev.GetBooleanPortValue_Sync(
                    VisionPort.CreateFromPath(VisionPorts.GAUGE_RESULT));
                GaugePass = ReadValue<bool>(passOp);

                var blobPassOp = dev.GetBooleanPortValue_Sync(
                    VisionPort.CreateFromPath(VisionPorts.BLOB_RESULT));
                BlobPass = ReadValue<bool>(blobPassOp);

                var listOp = dev.GetRealListPortValue_Sync(
                    VisionPort.CreateFromPath(VisionPorts.GAUGE_DISTANCE_LIST));
                var list = ReadValue<System.Collections.Generic.List<double>>(listOp);
                if (list != null && list.Count > 0)
                    MeasuredValue = list[0].ToString("0.0");
            }
            catch { /* leave the last known values in place */ }
        }

        // ===================== USER (login + permissions) =====================
        // IUserService is the ONLY part that gets swapped out later (LocalUserService -> AD/MSAL).
        // Everything below (permission checks, audit, UI blocking) stays unchanged.
        private readonly IUserService _userService = new LocalUserService();

        private AuthenticatedUser _user;
        public AuthenticatedUser User
        {
            get => _user;
            set
            {
                SetProperty(ref _user, value);
                OnPropertyChanged(nameof(IsLoggedIn));
                OnPropertyChanged(nameof(LoginButtonText));
                OnPropertyChanged(nameof(CurrentUser));
                OnPropertyChanged(nameof(CanOperate));
                OnPropertyChanged(nameof(CanEditGauge));
                OnPropertyChanged(nameof(CanEditBlob));
                OnPropertyChanged(nameof(CanSaveProgram));
                OnPropertyChanged(nameof(CanViewAudit));
            }
        }

        public bool IsLoggedIn => User != null;
        public string LoginButtonText => IsLoggedIn ? "Log out" : "Log in";
        public string CurrentUser => User?.Username ?? "";

        // Permissions - the UI binds IsEnabled to these.
        public bool CanOperate => User?.Has(Permission.CanOperate) ?? false;
        public bool CanEditGauge => User?.Has(Permission.CanEditGauge) ?? false;
        public bool CanEditBlob => User?.Has(Permission.CanEditBlob) ?? false;
        public bool CanSaveProgram => User?.Has(Permission.CanSaveProgram) ?? false;
        public bool CanViewAudit => User?.Has(Permission.CanViewAudit) ?? false;

        public RelayCommand LoginCommand { get; }
        public RelayCommand SaveProgramCommand { get; }

        // Username + role for the audit log: e.g. "tekniker (Tekniker)", otherwise "unknown".
        private string AuditUser => User?.AuditName ?? "unknown";

        private void DoLogin()
        {
            if (IsLoggedIn)
            {
                AuditLogger.Log(AuditUser, "Login", "Log out", Title);
                User = null;
                return;
            }

            var dlg = new LoginWindow(_userService) { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() == true)
            {
                User = dlg.AuthenticatedUser;
                AuditLogger.Log(AuditUser, "Login", "Log in", Title);
            }
        }

        private void DoSaveProgram()
        {
            if (!CanSaveProgram) { MessageBox.Show("You do not have permission to save the program."); return; }

            var dev = Device?.VisionDevice;
            if (dev == null) { MessageBox.Show("No connection to the camera."); return; }

            try
            {
                var op = dev.SaveProgram_Sync(VisionPort.CreateFromPath("Inspection"), 1);

                if (ReadSuccess(op))
                {
                    HasUnsavedChanges = false;
                    AuditLogger.Log(AuditUser, "Program", "Save program", Title, result: "OK");
                    MessageBox.Show("Program saved.");
                }
                else
                {
                    AuditLogger.Log(AuditUser, "Program", "Save program", Title, result: "ERROR");
                    MessageBox.Show("The program was not saved (the camera returned an error).");
                }
            }
            catch (Exception ex)
            {
                AuditLogger.Log(AuditUser, "Error", "Save program",
                                detail: Title + ": " + ex.Message);
                MessageBox.Show("Save program failed: " + ex.Message);
            }
        }

        // ===================== MANUAL OPERATION =====================
        public RelayCommand TriggerCommand { get; }
        public RelayCommand OnlineCommand { get; }
        public RelayCommand OfflineCommand { get; }

        private void SetOnline(bool online)
        {
            if (!CanOperate) { MessageBox.Show("You do not have permission to operate the camera."); return; }

            try
            {
                bool wasOnline = ReadValue<bool>(
                    Device?.VisionDevice?.GetOnlineState_Sync());

                Device?.VisionDevice?.SetOnline_Sync(online);
                RefreshStatus();

                bool nowOnline = ReadValue<bool>(
                    Device?.VisionDevice?.GetOnlineState_Sync());

                if (wasOnline != nowOnline)
                    AuditLogger.Log(AuditUser, "Status",
                                    online ? "Online" : "Offline", Title,
                                    result: nowOnline == online ? "OK" : "ERROR");
            }
            catch (Exception ex)
            {
                AuditLogger.Log(AuditUser, "Error",
                                online ? "Online" : "Offline",
                                detail: Title + ": " + ex.Message);
                MessageBox.Show("Online/Offline failed: " + ex.Message);
            }
        }

        private void DoTrigger()
        {
            try
            {
                var dev = Device?.VisionDevice;
                if (dev == null) return;

                if (!CanOperate) { MessageBox.Show("You do not have permission to operate the camera."); return; }

                bool isOnline = ReadValue<bool>(dev.GetOnlineState_Sync());
                if (!isOnline)
                {
                    MessageBox.Show("The camera is offline. Go online before you trigger.");
                    return;
                }

                var curGauge = ReadGauge(dev);
                var curBlob = ReadBlob(dev);

                LogDiff("Gauge", _lastGauge, curGauge);
                LogDiff("Blob", _lastBlob, curBlob);

                if (curGauge != null) _lastGauge = curGauge;
                if (curBlob != null) _lastBlob = curBlob;

                dev.Trigger_Sync();
                System.Threading.Thread.Sleep(300);
                RefreshStatus();
                ExternalTrigger = !ExternalTrigger;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Trigger failed: " + ex.Message);
            }
        }
    }
}