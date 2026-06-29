// ===================== CertValidation.cs =====================
using System;
using System.DirectoryServices.Protocols;
using System.Security.Cryptography.X509Certificates;

namespace GUI_Library
{
    /// <summary>
    /// Applies the chosen certificate-validation policy to an LdapConnection's
    /// SecureSocketLayer session. Shared by AdUserService (login) and
    /// AdDirectoryService (fetch/validate) so the behaviour is identical.
    ///
    ///  - AcceptAll   : accept any server certificate (no identity check).
    ///  - TrustedCert : accept ONLY a cert whose thumbprint matches the pinned one.
    ///  - SystemTrust : validate against the machine trust store (the cert or its
    ///                  issuing CA must be trusted on this machine).
    /// </summary>
    public static class CertValidation
    {
        /// <summary>
        /// Strips spaces, colons and case from a thumbprint so pasted values
        /// (e.g. "ED B7 DF..." or "ed:b7:df...") compare correctly.
        /// </summary>
        public static string NormalizeThumbprint(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
                return "";

            var chars = new System.Text.StringBuilder(thumbprint.Length);
            foreach (char c in thumbprint)
            {
                if (Uri.IsHexDigit(c))
                    chars.Append(char.ToUpperInvariant(c));
            }
            return chars.ToString();
        }

        /// <summary>
        /// Sets the VerifyServerCertificate callback on the connection according to
        /// the chosen mode. Only call this when LDAPS is in use.
        /// </summary>
        public static void Apply(LdapConnection connection,
                                 CertValidationMode mode,
                                 string trustedThumbprint)
        {
            switch (mode)
            {
                case CertValidationMode.AcceptAll:
                    connection.SessionOptions.VerifyServerCertificate =
                        (conn, cert) => true;
                    break;

                case CertValidationMode.TrustedCert:
                    string pinned = NormalizeThumbprint(trustedThumbprint);
                    connection.SessionOptions.VerifyServerCertificate =
                        (conn, cert) =>
                        {
                            if (string.IsNullOrEmpty(pinned)) return false;
                            try
                            {
                                var x509 = new X509Certificate2(cert);
                                string actual = NormalizeThumbprint(x509.Thumbprint);
                                return string.Equals(actual, pinned, StringComparison.Ordinal);
                            }
                            catch
                            {
                                return false;
                            }
                        };
                    break;

                case CertValidationMode.SystemTrust:
                    // LdapConnection does NOT reliably fall back to system validation
                    // when no callback is set (it tends to reject the cert -> error 81),
                    // even when SChannel itself would accept it. So we set a callback
                    // that explicitly runs Windows' chain validation and returns the
                    // result. This honours the machine trust store as intended:
                    // the cert (or its issuing CA) must be trusted on this machine.
                    connection.SessionOptions.VerifyServerCertificate =
                        (conn, cert) =>
                        {
                            try
                            {
                                var x509 = new X509Certificate2(cert);
                                using (var chain = new X509Chain())
                                {
                                    // Internal CAs and self-signed certs rarely have a
                                    // working revocation list, so skip revocation checks.
                                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                                    return chain.Build(x509);
                                }
                            }
                            catch
                            {
                                return false;
                            }
                        };
                    break;
            }
        }
    }
}