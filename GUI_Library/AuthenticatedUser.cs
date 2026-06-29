// ===================== AuthenticatedUser.cs =====================
namespace GUI_Library
{
    /// <summary>
    /// The result of a successful login. Carries the username + permissions and provides
    /// a simple Has() check that the ViewModel and UI use to block/allow actions.
    ///
    /// Can be constructed either from a UserRole (LocalUserService)
    /// or directly from Permission flags (AdUserService).
    /// </summary>
    public class AuthenticatedUser
    {
        public string Username { get; }
        public Permission Permissions { get; }

        // Display name for UI/audit - set by constructor
        private readonly string _displayRole;

        /// <summary>
        /// Used by LocalUserService - constructs from a UserRole.
        /// </summary>
        public AuthenticatedUser(string username, UserRole role)
        {
            Username = username;
            Permissions = role.Permissions();
            _displayRole = role.DisplayName();
        }

        /// <summary>
        /// Used by AdUserService - constructs directly from Permission flags.
        /// </summary>
        public AuthenticatedUser(string username, Permission permissions)
        {
            Username = username;
            Permissions = permissions;
            _displayRole = ResolveDisplayName(permissions);
        }

        public bool Has(Permission p) => Permissions.HasFlag(p);

        // Name used in the audit log: "nisanth (Engineer)".
        public string AuditName => $"{Username} ({_displayRole})";

        /// <summary>
        /// Derives a display name from the permission flags for AD users, matching the
        /// three roles (Operator / Engineer / Admin). Admin is checked first because it
        /// is a superset of the others.
        /// </summary>
        private static string ResolveDisplayName(Permission p)
        {
            if (p.HasFlag(Permission.CanViewAudit)) return "Admin";
            if (p.HasFlag(Permission.CanEditGauge)) return "Operator";
            if (p.HasFlag(Permission.CanEditBlob)) return "Engineer";
            if (p.HasFlag(Permission.CanOperate)) return "Operator";
            return "Unknown";
        }
    }
}