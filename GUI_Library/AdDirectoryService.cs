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
    /// PrincipalContext - so we can control port and choose LDAP vs LDAPS.
    /// Bind is always Basic (simple bind): on LDAP it is unencrypted, on LDAPS
    /// it is protected by TLS. No Kerberos, so it works on non-domain-joined PCs.
    /// </summary>
    public static class AdDirectoryService
    {
        /// <summary>
        /// Builds and binds an LdapConnection with the supplied credentials.
        /// Caller owns the connection and must dispose it. Throws on bad
        /// credentials or an unreachable server.
        /// </summary>
        private static LdapConnection CreateConnection(
            string server, int port, bool useLdaps,
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
                // Internal AD servers often have self-signed / unverifiable certs.
                // On a closed switch network we accept any server certificate so
                // LDAPS works out of the box. NOTE: this removes MITM protection.
                connection.SessionOptions.VerifyServerCertificate =
                    (conn, cert) => true;
            }

            connection.Credential = new NetworkCredential(user, password);
            connection.Bind(); // throws on bad credentials / unreachable server
            return connection;
        }

        /// <summary>
        /// Turns "APtest.local" into "DC=APtest,DC=local" for the search base.
        /// </summary>
        private static string DomainToBaseDn(string domain)
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
        /// <param name="server">AD server IP or hostname (e.g. 192.168.1.11)</param>
        /// <param name="domain">AD domain (e.g. APtest.local) - used for the search base</param>
        /// <param name="port">Port to use (e.g. 389 or 636)</param>
        /// <param name="useLdaps">True for LDAPS (TLS), false for plain LDAP</param>
        /// <param name="adminUser">An AD user allowed to read the directory</param>
        /// <param name="adminPassword">That user's password</param>
        /// <param name="error">Out: error message if the call failed, otherwise null</param>
        /// <returns>Sorted list of group names. Empty list on failure (see error).</returns>
        public static List<string> GetAllGroups(
            string server, string domain, int port, bool useLdaps,
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

            try
            {
                using (var connection = CreateConnection(
                    server, port, useLdaps, adminUser.Trim(), adminPassword))
                {
                    string baseDn = DomainToBaseDn(domain);

                    // All group objects. Page the results so large domains work.
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

                        // Find the page cookie to continue, if any.
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
            catch (Exception ex)
            {
                error = ex.Message;
                result.Clear();
            }

            return result;
        }

        /// <summary>
        /// Validates a configuration before it is saved. Checks that:
        ///  1. the admin credentials can connect to the given server/domain, AND
        ///  2. that admin user is actually a member of the chosen admin group.
        ///
        /// This prevents saving a wrong IP/domain/port (lock-out) and stops you
        /// from choosing an admin group you are not in.
        /// </summary>
        /// <returns>True if the configuration is valid. On false, 'error' explains why.</returns>
        public static bool ValidateAdminConfig(
            string server, string domain, int port, bool useLdaps,
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

            try
            {
                using (var connection = CreateConnection(
                    server, port, useLdaps, adminUser.Trim(), adminPassword))
                {
                    string baseDn = DomainToBaseDn(domain);

                    // 1. Find the admin user by sAMAccountName and read its group memberships.
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

                    // 2. Check the user's group memberships for the chosen admin group.
                    //    memberOf holds full DNs like "CN=NZVISADM,OU=...,DC=...".
                    if (userEntry.Attributes.Contains("memberOf"))
                    {
                        foreach (var value in userEntry.Attributes["memberOf"].GetValues(typeof(string)))
                        {
                            string dn = value as string;
                            if (DnMatchesGroup(dn, adminGroup))
                                return true; // valid: connected AND in the admin group
                        }
                    }

                    error = "The user '" + adminUser.Trim() +
                            "' is not a member of the chosen admin group '" +
                            adminGroup + "'. Choose a group you belong to.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                // Wrong IP, wrong domain, wrong port, or wrong credentials all land here.
                error = "Could not validate against the server: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// True if a full group DN ("CN=NZVISADM,OU=...") refers to the given
        /// short group name ("NZVISADM").
        /// </summary>
        private static bool DnMatchesGroup(string dn, string groupName)
        {
            if (string.IsNullOrWhiteSpace(dn) || string.IsNullOrWhiteSpace(groupName))
                return false;

            // First RDN is "CN=<name>". Pull out <name> and compare.
            string first = dn.Split(',')[0];
            int eq = first.IndexOf('=');
            if (eq < 0) return false;

            string cn = first.Substring(eq + 1).Trim();
            return cn.Equals(groupName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Escapes characters that are special inside an LDAP search filter.
        /// </summary>
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
}