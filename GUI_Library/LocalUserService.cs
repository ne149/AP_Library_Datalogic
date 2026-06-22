// ===================== LocalUserService.cs =====================
using System;
using System.Collections.Generic;

namespace GUI_Library
{
    /// <summary>
    /// TEMPORARY test implementation with a hardcoded user list.
    /// Lets you test the entire role/permission system NOW without a domain.
    ///
    /// ===> THIS IS THE ONLY FILE THAT GETS SWAPPED OUT for AD/MSAL later. <===
    ///
    /// Do not replace these with real secrets here - this is for testing only.
    /// </summary>
    public class LocalUserService : IUserService
    {
        private class Entry
        {
            public string Password;
            public UserRole Role;
        }

        // Test users. Username -> (password, role).
        private readonly Dictionary<string, Entry> _users =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase)
        {
            { "operator",   new Entry { Password = "123",      Role = UserRole.Operator   } },
            { "tekniker",   new Entry { Password = "tekniker", Role = UserRole.Tekniker   } },
            { "vision",     new Entry { Password = "vision",   Role = UserRole.VisionMand } },
            { "admin",      new Entry { Password = "Admin",    Role = UserRole.Admin      } },
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