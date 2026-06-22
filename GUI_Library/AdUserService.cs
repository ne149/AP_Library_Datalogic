// ===================== AdUserService.cs =====================
using System;
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;

namespace GUI_Library
{
    /// <summary>
    /// AD implementation of IUserService.
    /// Connects to an on-premise Active Directory, validates credentials,
    /// and maps AD group membership to Permission flags.
    ///
    /// Server, domain and the role->group mapping are NOT hardcoded here -
    /// they are passed in from the customer-specific project (e.g. SDK_GUI_Test1)
    /// so GUI_Library stays generic and reusable across customers.
    /// </summary>
    public class AdUserService : IUserService
    {
        private readonly string _server;
        private readonly string _domain;
        private readonly string _groupOperator;
        private readonly string _groupEngineer;
        private readonly string _groupAdmin;

        /// <param name="server">AD server IP or hostname (e.g. 192.168.1.11)</param>
        /// <param name="domain">AD domain (e.g. APtest.local)</param>
        /// <param name="groupOperator">AD group name that grants the Operator role</param>
        /// <param name="groupEngineer">AD group name that grants the Engineer role</param>
        /// <param name="groupAdmin">AD group name that grants the Admin role</param>
        public AdUserService(string server, string domain,
                             string groupOperator, string groupEngineer, string groupAdmin)
        {
            _server = server;
            _domain = domain;
            _groupOperator = groupOperator;
            _groupEngineer = groupEngineer;
            _groupAdmin = groupAdmin;
        }

        public AuthenticatedUser Login(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return null;

            try
            {
                // Context is created WITH the user's credentials so the same
                // connection can both validate the login AND read group membership
                // (needed when the PC is not domain-joined).
                using (var context = new PrincipalContext(
                    ContextType.Domain, _server, username.Trim(), password))
                {
                    bool valid = context.ValidateCredentials(username.Trim(), password);
                    if (!valid)
                        return null;

                    var groups = GetUserGroups(context, username.Trim());

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

        private List<string> GetUserGroups(PrincipalContext context, string username)
        {
            var groups = new List<string>();

            using (var user = UserPrincipal.FindByIdentity(context, username))
            {
                if (user == null) return groups;

                foreach (var group in user.GetGroups())
                {
                    groups.Add(group.Name);
                }
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
    }
}