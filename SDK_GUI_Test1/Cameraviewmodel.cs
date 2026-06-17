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
    /// EEN kamera. Egen VisionDeviceWrapper (een forbindelse).
    /// AddOn-kontroller (PropertyTolerance, PropertyBlock, ImageViewer) binder via endpoints.
    ///
    /// Logning af parameter-aendringer sker KUN ved trig (DoTrigger): vi sammenligner
    /// nuvaerende gauge- og blob-vaerdier med dem fra sidste trig og logger forskellen
    /// med den bruger der er logget ind. Justeringer frem/tilbage UDEN trig logges ikke -
    /// kun den vaerdi der faktisk bruges til en inspektion har audit-vaerdi.
    ///
    /// Adgang styres af rettigheder (Permission) fra den indloggede bruger:
    ///   CanOperate     -> online/offline/trig
    ///   CanEditGauge   -> gauge-tolerance (XAML: IsEnabled="{Binding CanEditGauge}")
    ///   CanEditBlob    -> blob min/max     (XAML: IsEnabled="{Binding CanEditBlob}")
    ///   CanSaveProgram -> gem program
    ///   CanViewAudit   -> se audit-log (fremtidig visning)
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

                // Online saa PropertyTolerance kan skrive aendringer til kameraet.
                Device.VisionDevice?.SetOnline_Sync(true);

                // Auto-reprocess: naar en AddOn-kontrol aendres -> Reprocess() kaldes
                // -> aendring skrives og kontroller genindlaeser. UDEN dette virker
                // PropertyTolerance-redigering ikke.
                AutoReprocess.ReprocessInstance = this;

                ExternalTrigger = !ExternalTrigger;

                // Baseline ved connect (udgangspunkt for foerste trig-sammenligning).
                _lastGauge = ReadGauge(Device.VisionDevice);
                _lastBlob = ReadBlob(Device.VisionDevice);
            }
            catch { /* vises som "Ikke forbundet" i status */ }

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

        // ===================== IDENTITET =====================
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
        // Kaldes af AddOn-frameworket naar en kontrol-vaerdi aendres af brugeren.
        // Vi re-evaluerer NUVAERENDE billede (tager IKKE nyt) saa pass/fail opdateres.
        // INGEN logning her - justering uden trig har ingen audit-vaerdi.
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

        // ===================== PARAMETER-SNAPSHOT (til trig-logning) =====================
        private class TolSnapshot
        {
            public double Nominal;
            public double Minus;   // for range: Start
            public double Plus;    // for range: End
            public bool Valid;
        }

        private TolSnapshot _lastGauge;
        private TolSnapshot _lastBlob;

        // Gauge: kommer tilbage som VisionTolerance (Nominal/Minus/Plus) - bekraeftet.
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

        // Blob "Required Number Of Blobs" er en Range 1D - laeses med GetRange1DPortValue_Sync
        // (IKKE GetTolerancePortValue_Sync). Feltnavne er StartValue/EndValue (bekraeftet),
        // men vi laeser defensivt via refleksion for robusthed.
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

        // Henter den foerste property der findes blandt de angivne navne, som double.
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

        // Sammenligner to snapshots felt-for-felt og logger kun det der aendrede sig.
        private void LogDiff(string tool, TolSnapshot oldT, TolSnapshot newT)
        {
            if (oldT == null || newT == null || !oldT.Valid || !newT.Valid) return;

            // Kun gauge har en aegte Nominal. Blob er en ren min/max-range.
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
        public string ConnectionText => IsConnected ? "Connected" : "Ikke forbundet";
        public string OnlineText => IsOnline ? "Online" : "Offline";

        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set => SetProperty(ref _hasUnsavedChanges, value);
        }

        // ===================== GAUGE RESULTAT =====================
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
            catch { /* lad sidste kendte vaerdier staa */ }
        }

        // ===================== BRUGER (login + rettigheder) =====================
        // IUserService er den ENESTE del der skiftes ud senere (LocalUserService -> AD/MSAL).
        // Alt herunder (rettighedstjek, audit, UI-spaerring) forbliver uaendret.
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
        public string LoginButtonText => IsLoggedIn ? "Log ud" : "Log ind";
        public string CurrentUser => User?.Username ?? "";

        // Rettigheder - UI binder IsEnabled til disse.
        public bool CanOperate => User?.Has(Permission.CanOperate) ?? false;
        public bool CanEditGauge => User?.Has(Permission.CanEditGauge) ?? false;
        public bool CanEditBlob => User?.Has(Permission.CanEditBlob) ?? false;
        public bool CanSaveProgram => User?.Has(Permission.CanSaveProgram) ?? false;
        public bool CanViewAudit => User?.Has(Permission.CanViewAudit) ?? false;

        public RelayCommand LoginCommand { get; }
        public RelayCommand SaveProgramCommand { get; }

        // Brugernavn + rolle til audit-log: fx "tekniker (Tekniker)", ellers "ukendt".
        private string AuditUser => User?.AuditName ?? "ukendt";

        private void DoLogin()
        {
            if (IsLoggedIn)
            {
                AuditLogger.Log(AuditUser, "Login", "Log ud", Title);
                User = null;
                return;
            }

            var dlg = new LoginWindow(_userService) { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() == true)
            {
                User = dlg.AuthenticatedUser;
                AuditLogger.Log(AuditUser, "Login", "Log ind", Title);
            }
        }

        private void DoSaveProgram()
        {
            if (!CanSaveProgram) { MessageBox.Show("Du har ikke rettighed til at gemme programmet."); return; }

            var dev = Device?.VisionDevice;
            if (dev == null) { MessageBox.Show("Ingen forbindelse til kameraet."); return; }

            try
            {
                var op = dev.SaveProgram_Sync(VisionPort.CreateFromPath("Inspection"), 1);

                if (ReadSuccess(op))
                {
                    HasUnsavedChanges = false;
                    AuditLogger.Log(AuditUser, "Program", "Gem program", Title, result: "OK");
                    MessageBox.Show("Program gemt.");
                }
                else
                {
                    AuditLogger.Log(AuditUser, "Program", "Gem program", Title, result: "FEJL");
                    MessageBox.Show("Programmet blev ikke gemt (kameraet returnerede fejl).");
                }
            }
            catch (Exception ex)
            {
                AuditLogger.Log(AuditUser, "Fejl", "Gem program",
                                detail: Title + ": " + ex.Message);
                MessageBox.Show("Gem program fejlede: " + ex.Message);
            }
        }

        // ===================== MANUEL BETJENING =====================
        public RelayCommand TriggerCommand { get; }
        public RelayCommand OnlineCommand { get; }
        public RelayCommand OfflineCommand { get; }

        private void SetOnline(bool online)
        {
            if (!CanOperate) { MessageBox.Show("Du har ikke rettighed til at betjene kameraet."); return; }

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
                                    result: nowOnline == online ? "OK" : "FEJL");
            }
            catch (Exception ex)
            {
                AuditLogger.Log(AuditUser, "Fejl",
                                online ? "Online" : "Offline",
                                detail: Title + ": " + ex.Message);
                MessageBox.Show("Online/Offline fejlede: " + ex.Message);
            }
        }

        private void DoTrigger()
        {
            try
            {
                var dev = Device?.VisionDevice;
                if (dev == null) return;

                if (!CanOperate) { MessageBox.Show("Du har ikke rettighed til at betjene kameraet."); return; }

                bool isOnline = ReadValue<bool>(dev.GetOnlineState_Sync());
                if (!isOnline)
                {
                    MessageBox.Show("Kameraet er offline. Gaa online foer du trigger.");
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
                MessageBox.Show("Trigger fejlede: " + ex.Message);
            }
        }
    }
}