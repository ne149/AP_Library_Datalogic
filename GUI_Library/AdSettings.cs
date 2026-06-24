// ===================== AdSettings.cs =====================
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GUI_Library
{
    /// <summary>
    /// Customer-specific AD configuration: server, domain, optional port/protocol,
    /// and which AD group maps to which application role. Stored as JSON next to
    /// the running .exe so it survives restarts and is configured per installation.
    ///
    /// CONNECTION PROTOCOL:
    ///  - LDAP  (UseLdaps = false): no encryption. Simple bind, default port 389.
    ///  - LDAPS (UseLdaps = true) : TLS encrypts the whole connection, default 636.
    ///  Both use a simple (Basic) bind - no Kerberos - so it works reliably on
    ///  machines that are NOT domain-joined and avoids time-sync (Kerberos) issues.
    /// </summary>
    [DataContract]
    public class AdSettings
    {
        [DataMember(Name = "server")]
        public string Server { get; set; } = "";

        [DataMember(Name = "domain")]
        public string Domain { get; set; } = "";

        // Optional explicit port. 0 = auto (389 for LDAP, 636 for LDAPS).
        [DataMember(Name = "port")]
        public int Port { get; set; } = 0;

        // false = LDAP (unencrypted), true = LDAPS (TLS).
        [DataMember(Name = "useLdaps")]
        public bool UseLdaps { get; set; } = false;

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
        /// Returns the port to actually use: the explicit Port if set (> 0),
        /// otherwise the default for the chosen protocol (636 LDAPS / 389 LDAP).
        /// </summary>
        public int EffectivePort => Port > 0 ? Port : (UseLdaps ? 636 : 389);

        /// <summary>
        /// True once the minimum required fields are filled in. Used to decide
        /// whether the settings page should be open (first run) or Admin-only.
        /// </summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Server) &&
            !string.IsNullOrWhiteSpace(Domain) &&
            !string.IsNullOrWhiteSpace(GroupAdmin);

        // ---------- persistence ----------

        private static string FilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adsettings.json");

        /// <summary>
        /// Loads settings from disk. Returns an empty (unconfigured) AdSettings if
        /// the file does not exist or cannot be read - never throws. Old files
        /// without the new port/useLdaps fields load fine (defaults are used).
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