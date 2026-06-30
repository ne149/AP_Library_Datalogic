// ===================== CameraConfig.cs =====================
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;


namespace SDK_GUI_Test1
{
    /// <summary>
    /// One camera entry from cameras.json: the display name, IP address and SDK port.
    /// </summary>
    [DataContract]
    public class CameraEntry
    {
        [DataMember(Name = "name")]
        public string Name { get; set; } = "";

        [DataMember(Name = "ip")]
        public string Ip { get; set; } = "";

        [DataMember(Name = "port")]
        public int Port { get; set; }

        // Temporary to deactivate unused cameras to make the run faster
        [DataMember(Name = "active")]
        public bool Active { get; set; } = true;
    }

    /// <summary>
    /// The list of cameras the app should show, loaded from cameras.json next to
    /// the .exe.
    ///
    /// The customer can edit cameras.json to change a camera's name, IP or port,
    /// or to add/remove cameras — no code change or rebuild needed. If the file
    /// does not exist yet (e.g. first run), it is created with a default list so
    /// the app always has something to show and a file ready to edit.
    /// </summary>
    [DataContract]
    public class CameraConfig
    {
        [DataMember(Name = "cameras")]
        public List<CameraEntry> Cameras { get; set; } = new List<CameraEntry>();

        // ---------- persistence ----------

        private static string FilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cameras.json");

        /// <summary>
        /// Loads the camera list from disk. If the file is missing it is created
        /// with a default list. Never throws — on any read error it falls back to
        /// the default list so the app still starts.
        /// </summary>
        public static CameraConfig Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    var def = Default();
                    def.Save();      // write it so the customer has a file to edit
                    return def;
                }

                using (var fs = File.OpenRead(FilePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(CameraConfig));
                    var config = (CameraConfig)serializer.ReadObject(fs);
                    if (config == null || config.Cameras == null || config.Cameras.Count == 0)
                        return Default();
                    return config;
                }
            }
            catch
            {
                return Default();
            }
        }

        /// <summary>Saves the camera list to disk. Returns false if the write failed.</summary>
        public bool Save()
        {
            try
            {
                using (var ms = new MemoryStream())
                {
                    var serializer = new DataContractJsonSerializer(typeof(CameraConfig));
                    serializer.WriteObject(ms, this);
                    File.WriteAllBytes(FilePath, ms.ToArray());
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The built-in default list, used when cameras.json is missing.</summary>
        private static CameraConfig Default()
        {
            return new CameraConfig
            {
                Cameras = new List<CameraEntry>
                {
                    new CameraEntry { Name = "Label Inspection : Camera 1", Ip = "192.168.1.128", Port = 10001, Active = true },
                    new CameraEntry { Name = "Camera 2", Ip = "192.168.1.129", Port = 10010, Active = false },
                    new CameraEntry { Name = "Camera 3", Ip = "192.168.1.130", Port = 10010, Active = false },
                    new CameraEntry { Name = "Camera 4", Ip = "192.168.1.131", Port = 10010, Active = false },
                    new CameraEntry { Name = "Camera 5", Ip = "192.168.1.132", Port = 10010, Active = false }
                }
            };
        }
    }
}