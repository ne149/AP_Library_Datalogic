// ===================== AdUserService.cs =====================
using System;
using System.Collections.Generic;
using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace GUI_Library
{
    /// <summary>
    /// AD implementation of IUserService.
    /// Connects to an on-premise Active Directory, validates credentials, and maps
    /// AD group membership to Permission flags.
    ///
    /// Server, domain, port/protocol, certificate policy and the role->group
    /// mapping are NOT hardcoded here - they are passed in from the customer-
    /// specific project so GUI_Library stays generic and reusable.
    ///
    /// Uses LdapConnection with a simple (Basic) bind - no Kerberos - so it works
    /// on machines that are not domain-joined. The user's own credentials are used
    /// to bind, which both validates the login AND lets us read their groups.
    /// </summary>
    public class AdAuthenticator : IUserService
    {
        private readonly string _server;
        private readonly string _domain;
        private readonly int _port;
        private readonly bool _useLdaps;
        private readonly CertValidationMode _certMode;
        private readonly string _trustedThumbprint;
        private readonly string _groupOperator;
        private readonly string _groupEngineer;
        private readonly string _groupAdmin;

        /// <param name="server">AD server IP or hostname (e.g. 192.168.1.11)</param>
        /// <param name="domain">AD domain (e.g. APtest.local)</param>
        /// <param name="groupOperator">AD group name that grants the Operator role</param>
        /// <param name="groupEngineer">AD group name that grants the Engineer role</param>
        /// <param name="groupAdmin">AD group name that grants the Admin role</param>
        /// <param name="port"> Port to connect (389 for LDAP / 636 for LDAPS).</param>
        /// <param name="useLdaps">True for LDAPS (TLS), false for plain LDAP.</param>
        /// <param name="certMode">How to validate the LDAPS server certificate.</param>
        /// <param name="trustedThumbprint">Pinned thumbprint for TrustedCert mode.</param>
        public AdAuthenticator(string server, string domain,
                             string groupOperator, string groupEngineer, string groupAdmin,
                             int port = 0, bool useLdaps = false,
                             CertValidationMode certMode = CertValidationMode.AcceptAll,
                             string trustedThumbprint = null)
        {
            _server = server;
            _domain = domain;
            _groupOperator = groupOperator;
            _groupEngineer = groupEngineer;
            _groupAdmin = groupAdmin;
            _useLdaps = useLdaps;
            _port = port > 0 ? port : (useLdaps ? 636 : 389);
            _certMode = certMode;
            _trustedThumbprint = CertValidation.NormalizeThumbprint(trustedThumbprint);
        }

        /// <summary>
        /// Simple login: returns the user on success, null on any failure
        /// (bad credentials OR unreachable server - the two are not distinguished).
        /// Kept for callers that don't need fallback logic.
        /// </summary>
        public AuthenticatedUser Login(string username, string password)
        {
            return TryLogin(username, password, out _);
        }

        /// <summary>
        /// Login that reports whether the server was REACHABLE, so a caller can
        /// decide whether to fall back to local users.
        ///
        ///   reachable = true,  return != null -> valid AD login
        ///   reachable = true,  return == null -> server answered, but bad
        ///                                         credentials or no mapped group
        ///   reachable = false, return == null -> could not reach/bind the server
        ///                                         at all (down, wrong port, TLS
        ///                                         failure, cert rejected, DNS, or
        ///                                         the server identity did not match)
        ///
        /// The reachable=true + null case must NOT trigger fallback: AD has spoken.
        /// </summary>
        public AuthenticatedUser TryLogin(string username, string password, out bool reachable)
        {
            reachable = false;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                reachable = true;
                return null;
            }

            LdapConnection connection = null;
            try
            {
                var identifier = new LdapDirectoryIdentifier(_server, _port);
                connection = new LdapConnection(identifier);
                connection.AuthType = AuthType.Basic;
                connection.SessionOptions.ProtocolVersion = 3;

                if (_useLdaps)
                {
                    connection.SessionOptions.SecureSocketLayer = true;
                    CertValidation.Apply(connection, _certMode, _trustedThumbprint);
                }

                connection.Credential = new NetworkCredential(username.Trim(), password);
                connection.Bind();

                // The server must be addressed by its exact short hostname or its IP.
                // A loose name (domain label, FQDN, etc.) is treated as not reachable,
                // so the user just sees the normal "could not reach the server" result.
                if (!AdReader.VerifyServerIdentity(connection, _server))
                {
                    connection.Dispose();
                    reachable = false;
                    return null;
                }
            }
            catch (LdapException ex)
            {
                connection?.Dispose();

                // 49 = invalidCredentials -> the server answered, so it IS reachable.
                if (ex.ErrorCode == 49)
                {
                    reachable = true;
                    return null;
                }

                // 81 = server down, 91 = cannot connect, plus TLS/cert/connect
                // failures -> the server could not be reached or trusted.
                reachable = false;
                return null;
            }
            catch (Exception)
            {
                // Anything else (DNS, socket, TLS, pinned-cert mismatch) -> unreachable.
                connection?.Dispose();
                reachable = false;
                return null;
            }

            // Bind succeeded AND identity verified -> the server is definitely reachable.
            reachable = true;
            try
            {
                using (connection)
                {
                    var groups = GetUserGroups(connection, username.Trim());
                    Permission permissions = MapGroupsToPermissions(groups);

                    if (permissions == Permission.None)
                        return null;

                    return new AuthenticatedUser(username.Trim(), permissions);
                }
            }
            catch (Exception)
            {
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

        // The permission that the users can have
        private Permission MapGroupsToPermissions(List<string> groups)
        {
            Permission permissions = Permission.None;

            foreach (var group in groups)
            {
                if (!string.IsNullOrWhiteSpace(_groupAdmin) &&
                    group.Equals(_groupAdmin, StringComparison.OrdinalIgnoreCase))
                {
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

        private static string DomainToBaseDn(string domain) // Setting DC in front of domain: required for LDAP
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

        private static string DnToCn(string dn) // setting the group name to only the name of the group (AD gives the full name, eg. CN=NZVISADM). We only want the group name ie after =.
        {
            if (string.IsNullOrWhiteSpace(dn)) return null;
            string first = dn.Split(',')[0];
            int eq = first.IndexOf('=');
            return eq < 0 ? null : first.Substring(eq + 1).Trim();
        }

        private static string EscapeFilter(string value) // Avoiding these values as LDAP uses them as hex codes. 
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