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

        // Name used in the audit log: "nisanth (Tekniker)".
        public string AuditName => $"{Username} ({_displayRole})";

        /// <summary>
        /// Derives a display name from the permission flags for AD users.
        /// </summary>
        private static string ResolveDisplayName(Permission p)
        {
            bool gauge = p.HasFlag(Permission.CanEditGauge);
            bool blob = p.HasFlag(Permission.CanEditBlob);
            bool audit = p.HasFlag(Permission.CanViewAudit);

            if (audit) return "Admin";
            if (gauge && blob) return "Vision A+E";
            if (gauge) return "Vision A";
            if (blob) return "Vision E";
            if (p.HasFlag(Permission.CanOperate)) return "Operator";
            return "Unknown";
        }
    }
}