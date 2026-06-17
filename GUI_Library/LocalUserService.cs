// ===================== LocalUserService.cs =====================
using System;
using System.Collections.Generic;

namespace GUI_Library
{
    /// <summary>
    /// MIDLERTIDIG test-implementering med en hardkodet brugerliste.
    /// Lader dig teste hele rolle-/rettighedssystemet NU uden domaene.
    ///
    /// ===> DETTE ER DEN ENESTE FIL DER SKIFTES UD med AD/MSAL senere. <===
    ///
    /// Erstat ikke koder med rigtige hemmeligheder her - det er kun til test.
    /// </summary>
    public class LocalUserService : IUserService
    {
        private class Entry
        {
            public string Password;
            public UserRole Role;
        }

        // Test-brugere. Brugernavn -> (kode, rolle).
        private readonly Dictionary<string, Entry> _users =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase)
        {
            { "operator",   new Entry { Password = "123", Role = UserRole.Operator   } },
            { "tekniker",   new Entry { Password = "tekniker", Role = UserRole.Tekniker   } },
            { "vision",     new Entry { Password = "vision", Role = UserRole.VisionMand } },
            { "admin",      new Entry { Password = "Admin", Role = UserRole.Admin      } },
        };

        public AuthenticatedUser Login(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;

            if (_users.TryGetValue(username.Trim(), out var entry)
                && entry.Password == password)
            {
                return new AuthenticatedUser(username.Trim(), entry.Role);
            }
            return null;
        }
    }
}