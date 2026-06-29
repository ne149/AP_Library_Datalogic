// ===================== AdDirectoryService.cs =====================
using System;
using System.Collections.Generic;
using System.DirectoryServices.Protocols;
using System.Net;

namespace GUI_Library
{
    /// <summary>
    /// Helper for reading directory metadata from AD (separate from login).
    /// Used by the settings page so the customer's admin can connect with their
    /// credentials and pull the list of groups that exist in their domain, and to
    /// validate the configuration before it is saved.
    ///
    /// Uses System.DirectoryServices.Protocols (LdapConnection) - NOT
    /// PrincipalContext - so we can control port, choose LDAP vs LDAPS, and apply
    /// the chosen certificate-validation policy. Bind is always Basic (simple bind).
    /// </summary>
    public static class AdReader
    {
        /// <summary>
        /// Builds and binds an LdapConnection with the supplied credentials and
        /// certificate policy. Caller owns the connection and must dispose it.
        /// Throws on bad credentials, an unreachable server, or a rejected cert.
        ///
        /// After binding, enforces that the server was reached using its EXACT
        /// short hostname or its IP address. If not, the connection is rejected
        /// the same way an unreachable server is (a loose name such as the domain
        /// label must not be accepted, even though AD's locator allows it).
        /// </summary>
        private static LdapConnection CreateConnection(
            string server, int port, bool useLdaps,
            CertValidationMode certMode, string trustedThumbprint,
            string user, string password)
        {
            var identifier = new LdapDirectoryIdentifier(server, port);
            var connection = new LdapConnection(identifier)
            {
                AuthType = AuthType.Basic
            };

            connection.SessionOptions.ProtocolVersion = 3;

            if (useLdaps)
            {
                connection.SessionOptions.SecureSocketLayer = true;
                CertValidation.Apply(connection, certMode, trustedThumbprint);
            }

            connection.Credential = new NetworkCredential(user, password);
            connection.Bind(); // Login to AD: throws on bad credentials / unreachable / cert reject

            // The server must be addressed by its exact short hostname or its IP.
            // A loose name (domain label, FQDN, etc.) is treated as not reachable.
            if (!VerifyServerIdentity(connection, server))
            {
                connection.Dispose();
                throw new ServerIdentityException();
            }

            return connection;
        }

        /// <summary>
        /// Returns true if 'userInput' (the AD Server field) is either a literal IP
        /// address OR exactly the server's own short hostname. Blocks reaching the
        /// server via loose names that AD's domain-locator would otherwise accept
        /// (e.g. "aptest" or the FQDN). The real name is read back from the server
        /// itself (rootDSE's dnsHostName), so nothing is hardcoded.
        /// Public so AdUserService can reuse the exact same rule for login.
        /// </summary>
        public static bool VerifyServerIdentity(LdapConnection connection, string userInput)
        {
            string input = userInput?.Trim();
            if (string.IsNullOrWhiteSpace(input))
                return false;

            // A literal IP address is always allowed.
            if (IPAddress.TryParse(input, out _))
                return true;

            // Otherwise it must exactly match the server's SHORT hostname.
            string realShortName = GetServerShortName(connection);
            if (string.IsNullOrWhiteSpace(realShortName))
                return false; // fail safe: cannot determine the real name -> reject

            return string.Equals(input, realShortName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads the server's own DNS host name from rootDSE (the 'dnsHostName'
        /// attribute, e.g. "aptestdc.aptest.local") and returns the short part
        /// before the first dot ("aptestdc"). Returns null if it cannot be read.
        /// </summary>
        private static string GetServerShortName(LdapConnection connection)
        {
            try
            {
                var request = new SearchRequest(
                    null,                 // rootDSE: empty base DN
                    "(objectClass=*)",
                    SearchScope.Base,
                    "dnsHostName");

                var response = (SearchResponse)connection.SendRequest(request);
                if (response.Entries.Count == 0)
                    return null;

                var entry = response.Entries[0];
                if (!entry.Attributes.Contains("dnsHostName"))
                    return null;

                string fqdn = entry.Attributes["dnsHostName"][0]?.ToString();
                if (string.IsNullOrWhiteSpace(fqdn))
                    return null;

                int dot = fqdn.IndexOf('.');
                return dot > 0 ? fqdn.Substring(0, dot) : fqdn; // Reading first part of FQDN
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The word used in user-facing error messages for the current protocol,
        /// so an LDAPS failure says "LDAPS" rather than the generic "LDAP".
        /// </summary>
        private static string ProtocolLabel(bool useLdaps) => useLdaps ? "LDAPS" : "LDAP";

        /// <summary>
        /// Builds the "could not reach the server" message, adding the certificate
        /// thumbprint as a possible cause when pinning (TrustedCert) is in use,
        /// since a wrong thumbprint also surfaces as a connection failure.
        /// </summary>
        private static string UnreachableMessage(bool useLdaps, CertValidationMode certMode)
        {
            string label = ProtocolLabel(useLdaps);
            string baseMsg = "Could not reach the " + label +
                             " server. Check the server address, port, and that " +
                             label + " is enabled on the server";

            if (useLdaps && certMode == CertValidationMode.TrustedCert)
                baseMsg += ", and the certificate thumbprint";

            return baseMsg + ".";
        }

        /// <summary>
        /// Validates that, when TrustedCert mode is selected over LDAPS, a
        /// thumbprint has actually been entered. Returns false + error if not.
        /// </summary>
        private static bool CheckCertConfig(bool useLdaps, CertValidationMode certMode,
                                            string trustedThumbprint, out string error)
        {
            error = null;
            if (useLdaps &&
                certMode == CertValidationMode.TrustedCert &&
                string.IsNullOrWhiteSpace(CertValidation.NormalizeThumbprint(trustedThumbprint))) //Thumbprint mode is chosen, but thumbprint is empty
            {
                error = "Certificate validation is set to 'Trusted certificate', " +
                        "but no certificate thumbprint has been entered. " +
                        "Enter the server certificate's thumbprint first.";
                return false;
            }
            return true;
        }

        private static string DomainToBaseDn(string domain) // 
        {
            if (string.IsNullOrWhiteSpace(domain))
                return "";

            var parts = domain.Split('.');
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i])) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append("DC=").Append(parts[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Connects to AD with the supplied admin credentials and returns the
        /// names of all groups in the domain.
        /// </summary>
        public static List<string> GetAllGroups(
            string server, string domain, int port, bool useLdaps,
            CertValidationMode certMode, string trustedThumbprint,
            string adminUser, string adminPassword,
            out string error)
        {
            error = null;
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(domain) ||
                string.IsNullOrWhiteSpace(adminUser) ||
                string.IsNullOrWhiteSpace(adminPassword))
            {
                error = "Server, domain, username and password are required.";
                return result;
            }

            if (!CheckCertConfig(useLdaps, certMode, trustedThumbprint, out error))
                return result;

            try
            {
                using (var connection = CreateConnection(
                    server, port, useLdaps, certMode, trustedThumbprint,
                    adminUser.Trim(), adminPassword))
                {
                    string baseDn = DomainToBaseDn(domain);

                    var request = new SearchRequest(
                        baseDn,
                        "(objectClass=group)",
                        SearchScope.Subtree,
                        "cn");

                    var pageControl = new PageResultRequestControl(500);
                    request.Controls.Add(pageControl);

                    while (true)
                    {
                        var response = (SearchResponse)connection.SendRequest(request);

                        foreach (SearchResultEntry entry in response.Entries)
                        {
                            if (entry.Attributes.Contains("cn"))
                            {
                                string name = entry.Attributes["cn"][0]?.ToString();
                                if (!string.IsNullOrWhiteSpace(name))
                                    result.Add(name);
                            }
                        }

                        PageResultResponseControl pageResponse = null;
                        foreach (var ctrl in response.Controls)
                        {
                            pageResponse = ctrl as PageResultResponseControl;
                            if (pageResponse != null) break;
                        }

                        if (pageResponse == null || pageResponse.Cookie.Length == 0)
                            break;

                        pageControl.Cookie = pageResponse.Cookie;
                    }
                }

                result.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (ServerIdentityException)
            {
                // Wrong server/domain value - report it like any other connection failure.
                // useLdaps is a bool, true for ldaps, false for ldap. 
                error = UnreachableMessage(useLdaps, certMode);
                result.Clear();
            }
            catch (LdapException lex)
            {
                if (lex.ErrorCode == 49)
                    error = "The username or password is incorrect.";
                else
                    error = UnreachableMessage(useLdaps, certMode);
                result.Clear();
            }
            catch (Exception)
            {
                error = UnreachableMessage(useLdaps, certMode);
                result.Clear();
            }

            return result;
        }

        /// <summary>
        /// Validates a configuration before it is saved. Checks that the admin
        /// credentials can connect AND that the admin user is a member of the
        /// chosen admin group (so the person saving cannot lock themselves out).
        /// </summary>
        public static bool ValidateAdminConfig(
            string server, string domain, int port, bool useLdaps,
            CertValidationMode certMode, string trustedThumbprint,
            string adminUser, string adminPassword,
            string adminGroup,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(domain) ||
                string.IsNullOrWhiteSpace(adminUser) ||
                string.IsNullOrWhiteSpace(adminPassword) ||
                string.IsNullOrWhiteSpace(adminGroup))
            {
                error = "Server, domain, admin username, password and admin group are required.";
                return false;
            }

            if (!CheckCertConfig(useLdaps, certMode, trustedThumbprint, out error))
                return false;

            try
            {
                using (var connection = CreateConnection(
                    server, port, useLdaps, certMode, trustedThumbprint,
                    adminUser.Trim(), adminPassword))
                {
                    string baseDn = DomainToBaseDn(domain);

                    var userRequest = new SearchRequest(
                        baseDn,
                        "(&(objectClass=user)(sAMAccountName=" + EscapeFilter(adminUser.Trim()) + "))",
                        SearchScope.Subtree,
                        "memberOf");

                    var userResponse = (SearchResponse)connection.SendRequest(userRequest);

                    if (userResponse.Entries.Count == 0)
                    {
                        error = "The admin user could not be found on the server.";
                        return false;
                    }

                    var userEntry = userResponse.Entries[0];

                    if (userEntry.Attributes.Contains("memberOf"))
                    {
                        foreach (var value in userEntry.Attributes["memberOf"].GetValues(typeof(string)))
                        {
                            string dn = value as string;
                            if (DnMatchesGroup(dn, adminGroup))
                                return true;
                        }
                    }

                    error = "The user '" + adminUser.Trim() +
                            "' is not a member of the chosen admin group '" +
                            adminGroup + "'. Choose a group you belong to.";
                    return false;
                }
            }
            catch (ServerIdentityException)
            {
                // Wrong server/domain value - report it like any other connection failure.
                error = UnreachableMessage(useLdaps, certMode);
                return false;
            }
            catch (LdapException lex)
            {
                if (lex.ErrorCode == 49)
                    error = "The username or password is incorrect.";
                else
                    error = UnreachableMessage(useLdaps, certMode);
                return false;
            }
            catch (Exception)
            {
                error = UnreachableMessage(useLdaps, certMode);
                return false;
            }
        }

        private static bool DnMatchesGroup(string dn, string groupName) // Taking only the group name
        {
            if (string.IsNullOrWhiteSpace(dn) || string.IsNullOrWhiteSpace(groupName))
                return false;

            string first = dn.Split(',')[0];
            int eq = first.IndexOf('=');
            if (eq < 0) return false; 

            string cn = first.Substring(eq + 1).Trim();
            return cn.Equals(groupName, StringComparison.OrdinalIgnoreCase);
        }

        private static string EscapeFilter(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            return value
                .Replace("\\", "\\5c")
                .Replace("*", "\\2a")
                .Replace("(", "\\28")
                .Replace(")", "\\29")
                .Replace("\0", "\\00");
        }
    }

    /// <summary>
    /// Thrown internally when the AD Server field does not match the server's real
    /// identity. Caught by the callers and reported as an ordinary connection
    /// failure, so the user just sees the normal "could not reach the server"
    /// message rather than any technical detail.
    /// </summary>
    public class ServerIdentityException : Exception
    {
    }
}