using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SDK_GUI_Test1
{
    /// <summary>
    /// Holds all cameras. Each camera is independent (its own connection/status).
    /// The start page shows an overview; each camera also has its own tab.
    /// </summary>
    public class MainWindowViewModel : ObservableObject
    {
        public ObservableCollection<CameraViewModel> Cameras { get; }

        public MainWindowViewModel()
        {
            Cameras = new ObservableCollection<CameraViewModel>
            {
                // Active camera (the one we are working on now).
                new CameraViewModel("Label Inspection : Camera 1", "192.168.1.128", 10001, active: true),
                // Empty slots - added when the real cameras are connected.
                new CameraViewModel("Camera 2", "192.168.1.129", 10010, active: false),
                new CameraViewModel("Camera 3", "192.168.1.130", 10010, active: false),
                new CameraViewModel("Camera 4", "192.168.1.131", 10010, active: false),
                new CameraViewModel("Camera 5", "192.168.1.132", 10010, active: false),
            };
        }
    }
}