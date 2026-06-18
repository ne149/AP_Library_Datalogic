// ===================== AuthenticatedUser.cs =====================
namespace GUI_Library
{
    /// <summary>
    /// The result of a successful login. Carries the username + role and provides
    /// a simple Has() check that the ViewModel and UI use to block/allow actions.
    /// </summary>
    public class AuthenticatedUser
    {
        public string Username { get; }
        public UserRole Role { get; }

        public AuthenticatedUser(string username, UserRole role)
        {
            Username = username;
            Role = role;
        }

        public bool Has(Permission p) => Role.Permissions().HasFlag(p);

        // Name used in the audit log: "nisanth (Tekniker)".
        public string AuditName => $"{Username} ({Role.DisplayName()})";
    }
}