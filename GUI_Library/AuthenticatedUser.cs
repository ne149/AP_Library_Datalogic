// ===================== AuthenticatedUser.cs =====================
namespace GUI_Library
{
    /// <summary>
    /// Resultatet af et vellykket login. Baerer brugernavn + rolle og giver et
    /// enkelt Has()-tjek som ViewModel og UI bruger til at spaerre/tillade.
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

        // Navn der bruges i audit-loggen: "nisanth (Tekniker)".
        public string AuditName => $"{Username} ({Role.DisplayName()})";
    }
}