// ===================== AdDirectoryService.cs =====================
using System;
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;

namespace GUI_Library
{
    /// <summary>
    /// Helper for reading directory metadata from AD (separate from login).
    /// Used by the settings page so the customer's admin can connect with their
    /// credentials and pull the list of groups that exist in their domain -
    /// instead of typing group names by hand - and to validate the configuration
    /// before it is saved.
    /// </summary>
    public static class AdDirectoryService
    {
        /// <summary>
        /// Connects to AD with the supplied admin credentials and returns the
        /// names of all groups in the domain.
        /// </summary>
        /// <param name="server">AD server IP or hostname (e.g. 192.168.1.11)</param>
        /// <param name="domain">AD domain (e.g. APtest.local) - not strictly required for the bind</param>
        /// <param name="adminUser">An AD user allowed to read the directory</param>
        /// <param name="adminPassword">That user's password</param>
        /// <param name="error">Out: error message if the call failed, otherwise null</param>
        /// <returns>Sorted list of group names. Empty list on failure (see error).</returns>
        public static List<string> GetAllGroups(
            string server, string domain,
            string adminUser, string adminPassword,
            out string error)
        {
            error = null;
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(adminUser) ||
                string.IsNullOrWhiteSpace(adminPassword))
            {
                error = "Server, username and password are required.";
                return result;
            }

            try
            {
                // Bind WITH admin credentials so we are allowed to read the directory.
                // NOTE: we do NOT call ValidateCredentials here. On a PC that is not
                // domain-joined, ValidateCredentials on a credential-bound context can
                // wrongly fail (Negotiate/Kerberos issue). Instead we let the actual
                // search run - if the credentials are wrong, the search throws and we
                // report that in the catch block below.
                using (var context = new PrincipalContext(
                    ContextType.Domain, server, adminUser.Trim(), adminPassword))
                using (var searchTemplate = new GroupPrincipal(context))
                using (var searcher = new PrincipalSearcher(searchTemplate))
                {
                    foreach (var found in searcher.FindAll())
                    {
                        var grp = found as GroupPrincipal;
                        if (grp != null && !string.IsNullOrWhiteSpace(grp.Name))
                            result.Add(grp.Name);
                        found.Dispose();
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
        /// This is what prevents two mistakes:
        ///  - saving a wrong IP/domain (connection fails -> not saved), and
        ///  - choosing an admin group that the person setting it up is NOT in
        ///    (which would lock everyone out of the settings afterwards).
        /// </summary>
        /// <returns>True if the configuration is valid. On false, 'error' explains why.</returns>
        public static bool ValidateAdminConfig(
            string server, string domain,
            string adminUser, string adminPassword,
            string adminGroup,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(server) ||
                string.IsNullOrWhiteSpace(adminUser) ||
                string.IsNullOrWhiteSpace(adminPassword) ||
                string.IsNullOrWhiteSpace(adminGroup))
            {
                error = "Server, admin username, password and admin group are required.";
                return false;
            }

            try
            {
                using (var context = new PrincipalContext(
                    ContextType.Domain, server, adminUser.Trim(), adminPassword))
                {
                    // Find the admin user. If credentials/server are wrong, this throws
                    // (caught below) or returns null.
                    using (var user = UserPrincipal.FindByIdentity(context, adminUser.Trim()))
                    {
                        if (user == null)
                        {
                            error = "The admin user could not be found on the server.";
                            return false;
                        }

                        // Check the user is a member of the chosen admin group, so the
                        // person saving cannot lock themselves out.
                        foreach (var group in user.GetGroups())
                        {
                            if (!string.IsNullOrWhiteSpace(group.Name) &&
                                group.Name.Equals(adminGroup, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;  // valid: connected AND in the admin group
                            }
                        }

                        error = "The user '" + adminUser.Trim() +
                                "' is not a member of the chosen admin group '" +
                                adminGroup + "'. Choose a group you belong to.";
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                // Wrong IP, wrong domain, or wrong credentials all land here.
                error = "Could not validate against the server: " + ex.Message;
                return false;
            }
        }
    }
}