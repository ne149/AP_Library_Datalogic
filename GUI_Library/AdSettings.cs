// ===================== AdSettings.cs =====================
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace GUI_Library
{
    /// <summary>
    /// Customer-specific AD configuration: server, domain and which AD group maps
    /// to which application role. Stored as JSON next to the running .exe so it
    /// survives restarts and is configured per installation (per customer).
    ///
    /// GUI_Library stays generic: it knows the SHAPE of the settings (three roles)
    /// but not the actual group names - those are entered by the customer's admin
    /// in the settings page and saved here.
    ///
    /// First run: the file does not exist yet, IsConfigured is false, and the
    /// settings page is open to everyone so the admin can do the initial setup.
    /// After setup: the file exists, IsConfigured is true, and the settings page
    /// is restricted to Admin only.
    /// </summary>
    [DataContract]
    public class AdSettings
    {
        [DataMember(Name = "server")]
        public string Server { get; set; } = "";

        [DataMember(Name = "domain")]
        public string Domain { get; set; } = "";

        // AD group name that grants the Operator role (operate + gauge + save).
        [DataMember(Name = "groupOperator")]
        public string GroupOperator { get; set; } = "";

        // AD group name that grants the Engineer role (operate + blob + save).
        [DataMember(Name = "groupEngineer")]
        public string GroupEngineer { get; set; } = "";

        // AD group name that grants the Admin role (everything).
        [DataMember(Name = "groupAdmin")]
        public string GroupAdmin { get; set; } = "";

        /// <summary>
        /// True once the minimum required fields are filled in. Used to decide
        /// whether the settings page should be open (first run) or Admin-only.
        /// </summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Server) &&
            !string.IsNullOrWhiteSpace(Domain) &&
            !string.IsNullOrWhiteSpace(GroupAdmin);

        // ---------- persistence ----------

        // The settings file lives next to the .exe (per-installation).
        private static string FilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adsettings.json");

        /// <summary>
        /// Loads settings from disk. Returns an empty (unconfigured) AdSettings if
        /// the file does not exist or cannot be read - never throws.
        /// </summary>
        public static AdSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new AdSettings();

                using (var fs = File.OpenRead(FilePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AdSettings));
                    var settings = (AdSettings)serializer.ReadObject(fs);
                    return settings ?? new AdSettings();
                }
            }
            catch
            {
                return new AdSettings();
            }
        }

        /// <summary>
        /// Saves settings to disk. Returns false if the write failed.
        /// </summary>
        public bool Save()
        {
            try
            {
                using (var ms = new MemoryStream())
                {
                    var serializer = new DataContractJsonSerializer(typeof(AdSettings));
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
    }
}