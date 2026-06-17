using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SDK_GUI_Test1
{
    /// <summary>
    /// Holder alle kameraer. Hvert kamera er uafhaengigt (egen forbindelse/status).
    /// Startside viser et overblik; hver kamera har desuden sin egen fane.
    /// </summary>
    public class MainWindowViewModel : ObservableObject
    {
        public ObservableCollection<CameraViewModel> Cameras { get; }

        public MainWindowViewModel()
        {
            Cameras = new ObservableCollection<CameraViewModel>
            {
                // Aktivt kamera (det vi arbejder paa nu).
                new CameraViewModel("Label Inspection : Kamera 1", "192.168.1.128", 10001, active: true),

                // Tomme pladser - tilfoejes naar de rigtige kameraer kobles paa.
                new CameraViewModel("Kamera 2", "192.168.1.129", 10010, active: false),
                new CameraViewModel("Kamera 3", "192.168.1.130", 10010, active: false),
            };
        }
    }
}