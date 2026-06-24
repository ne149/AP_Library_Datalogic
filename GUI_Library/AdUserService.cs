// ===================== AdUserService.cs =====================
using System;
using System.Collections.Generic;
using System.DirectoryServices.Protocols;
using System.Net;

namespace GUI_Library
{
    /// <summary>
    /// AD implementation of IUserService.
    /// Connects to an on-premise Active Directory, validates credentials, and maps
    /// AD group membership to Permission flags.
    ///
    /// Server, domain, port/protocol and the role->group mapping are NOT hardcoded
    /// here - they are passed in from the customer-specific project so GUI_Library
    /// stays generic and reusable across customers.
    ///
    /// Uses LdapConnection with a simple (Basic) bind - no Kerberos - so it works
    /// on machines that are not domain-joined. The user's own credentials are used
    /// to bind, which both validates the login AND lets us read their groups.
    /// </summary>
    public class AdUserService : IUserService
    {
        private readonly string _server;
        private readonly string _domain;
        private readonly int _port;
        private readonly bool _useLdaps;
        private readonly string _groupOperator;
        private readonly string _groupEngineer;
        private readonly string _groupAdmin;

        /// <param name="server">AD server IP or hostname (e.g. 192.168.1.11)</param>
        /// <param name="domain">AD domain (e.g. APtest.local)</param>
        /// <param name="groupOperator">AD group name that grants the Operator role</param>
        /// <param name="groupEngineer">AD group name that grants the Engineer role</param>
        /// <param name="groupAdmin">AD group name that grants the Admin role</param>
        /// <param name="port">Port to use. 0 = auto (389 LDAP / 636 LDAPS).</param>
        /// <param name="useLdaps">True for LDAPS (TLS), false for plain LDAP.</param>
        public AdUserService(string server, string domain,
                             string groupOperator, string groupEngineer, string groupAdmin,
                             int port = 0, bool useLdaps = false)
        {
            _server = server;
            _domain = domain;
            _groupOperator = groupOperator;
            _groupEngineer = groupEngineer;
            _groupAdmin = groupAdmin;
            _useLdaps = useLdaps;
            _port = port > 0 ? port : (useLdaps ? 636 : 389);
        }

        public AuthenticatedUser Login(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return null;

            try
            {
                var identifier = new LdapDirectoryIdentifier(_server, _port);
                using (var connection = new LdapConnection(identifier))
                {
                    connection.AuthType = AuthType.Basic;
                    connection.SessionOptions.ProtocolVersion = 3;

                    if (_useLdaps)
                    {
                        connection.SessionOptions.SecureSocketLayer = true;
                        connection.SessionOptions.VerifyServerCertificate =
                            (conn, cert) => true;
                    }

                    // Bind WITH the user's credentials. If they are wrong, Bind throws
                    // and we return null (caught below) - this is the login check.
                    connection.Credential = new NetworkCredential(username.Trim(), password);
                    connection.Bind();

                    var groups = GetUserGroups(connection, username.Trim());

                    Permission permissions = MapGroupsToPermissions(groups);

                    if (permissions == Permission.None)
                        return null;

                    return new AuthenticatedUser(username.Trim(), permissions);
                }
            }
            catch (Exception)
            {
                // Bad credentials, unreachable server, or wrong port all land here.
                return null;
            }
        }

        /// <summary>
        /// Reads the short names of the groups the user is a member of, via the
        /// user's memberOf attribute.
        /// </summary>
        private List<string> GetUserGroups(LdapConnection connection, string username)
        {
            var groups = new List<string>();
            string baseDn = DomainToBaseDn(_domain);

            var request = new SearchRequest(
                baseDn,
                "(&(objectClass=user)(sAMAccountName=" + EscapeFilter(username) + "))",
                SearchScope.Subtree,
                "memberOf");

            var response = (SearchResponse)connection.SendRequest(request);
            if (response.Entries.Count == 0)
                return groups;

            var entry = response.Entries[0];
            if (!entry.Attributes.Contains("memberOf"))
                return groups;

            foreach (var value in entry.Attributes["memberOf"].GetValues(typeof(string)))
            {
                string dn = value as string;
                string cn = DnToCn(dn);
                if (!string.IsNullOrWhiteSpace(cn))
                    groups.Add(cn);
            }

            return groups;
        }

        private Permission MapGroupsToPermissions(List<string> groups)
        {
            Permission permissions = Permission.None;

            foreach (var group in groups)
            {
                if (!string.IsNullOrWhiteSpace(_groupAdmin) &&
                    group.Equals(_groupAdmin, StringComparison.OrdinalIgnoreCase))
                {
                    // Admin gets everything
                    return Permission.CanOperate
                         | Permission.CanEditGauge
                         | Permission.CanEditBlob
                         | Permission.CanSaveProgram
                         | Permission.CanViewAudit;
                }

                if (!string.IsNullOrWhiteSpace(_groupOperator) &&
                    group.Equals(_groupOperator, StringComparison.OrdinalIgnoreCase))
                {
                    permissions |= Permission.CanOperate
                                | Permission.CanEditGauge
                                | Permission.CanSaveProgram;
                }

                if (!string.IsNullOrWhiteSpace(_groupEngineer) &&
                    group.Equals(_groupEngineer, StringComparison.OrdinalIgnoreCase))
                {
                    permissions |= Permission.CanOperate
                                | Permission.CanEditBlob
                                | Permission.CanSaveProgram;
                }
            }

            return permissions;
        }

        // ---------- helpers ----------

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

        private static string DnToCn(string dn)
        {
            if (string.IsNullOrWhiteSpace(dn)) return null;
            string first = dn.Split(',')[0];
            int eq = first.IndexOf('=');
            return eq < 0 ? null : first.Substring(eq + 1).Trim();
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
}