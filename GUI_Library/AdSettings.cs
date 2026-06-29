// ===================== AdSettings.cs =====================
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GUI_Library
{
    /// <summary>
    /// How the client validates the AD server's TLS certificate when using LDAPS.
    /// Only relevant when UseLdaps is true; ignored for plain LDAP.
    /// </summary>
    public enum CertValidationMode
    {
        // Accept ANY server certificate. Encrypts the connection but does NOT
        // verify the server's identity (no MITM protection). Fine on a closed
        // network / for testing. This is the legacy behaviour.
        AcceptAll = 0,

        // Accept ONLY a certificate whose thumbprint matches TrustedThumbprint
        // (certificate pinning). MITM protection without needing a CA, but the
        // pinned thumbprint must be updated when the server cert is renewed.
        TrustedCert = 1,

        // Let Windows validate the certificate normally against the machine's
        // trust store. Requires the server cert (or its issuing CA) to be trusted
        // on THIS client machine. The "proper" option when an internal CA exists.
        SystemTrust = 2
    }

    /// <summary>
    /// Customer-specific AD configuration: server, domain, optional port/protocol,
    /// certificate-validation policy, and which AD group maps to which role.
    /// Stored as JSON next to the running .exe so it survives restarts and is
    /// configured per installation.
    ///
    /// CONNECTION PROTOCOL:
    ///  - LDAP  (UseLdaps = false): no encryption. Simple bind - port 389.
    ///  - LDAPS (UseLdaps = true) : TLS encrypts the whole connection - port 636.
    ///  Both use a simple (Basic) bind - no Kerberos - so it works reliably on
    ///  machines that are NOT domain-joined and avoids time-sync (Kerberos) issues.
    /// </summary>
    [DataContract] // This means that the class can be used with JSON (All DataMember ends in JSON)
    public class AdSettings
    {
        [DataMember(Name = "server")]
        public string Server { get; set; } = "";

        [DataMember(Name = "domain")]
        public string Domain { get; set; } = "";

        

        [DataMember(Name = "port")]
        public int Port { get; set; } = 0;

        // false = LDAP (unencrypted), true = LDAPS (TLS).
        [DataMember(Name = "useLdaps")]
        public bool UseLdaps { get; set; } = false;

        // How to validate the LDAPS server certificate. Stored as int so old
        // JSON files (without this field) load as 0 = AcceptAll = legacy behaviour.
        [DataMember(Name = "certValidationMode")]
        public int CertValidationModeRaw { get; set; } = (int)CertValidationMode.AcceptAll;

        // The certificate thumbprint to pin against when CertValidationMode is
        // TrustedCert. Hex string, case/space-insensitive when compared.
        [DataMember(Name = "trustedThumbprint")]
        public string TrustedThumbprint { get; set; } = "";

        // AD group name that grants the Operator role.
        [DataMember(Name = "groupOperator")]
        public string GroupOperator { get; set; } = "";

        // AD group name that grants the Engineer role.
        [DataMember(Name = "groupEngineer")]
        public string GroupEngineer { get; set; } = "";

        // AD group name that grants the Admin role.
        [DataMember(Name = "groupAdmin")]
        public string GroupAdmin { get; set; } = "";

        /// <summary>
        /// Translating cert modes between number and enum. Unknown values fall back to AcceptAll.
        /// </summary>
        public CertValidationMode CertValidationMode
        {
            get
            {
                return Enum.IsDefined(typeof(CertValidationMode), CertValidationModeRaw)
                    ? (CertValidationMode)CertValidationModeRaw
                    : CertValidationMode.AcceptAll;
            }
            set { CertValidationModeRaw = (int)value; }
        }

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
        /// without the new fields load fine (defaults are used).
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