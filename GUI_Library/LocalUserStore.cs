// ===================== LocalUserStore.cs =====================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace GUI_Library
{
    /// <summary>
    /// Holds the list of local users plus the master on/off toggle, and persists
    /// them as JSON next to the .exe (same pattern as AdSettings).
    ///
    /// Local users are a FALLBACK: they only matter when AD is unreachable, or when
    /// no AD is configured at all. The Enabled flag lets an admin switch the whole
    /// fallback off so it cannot be abused on a site with healthy AD. Default = off.
    ///
    /// Passwords are hashed with PBKDF2 (Rfc2898DeriveBytes, SHA-256) and a random
    /// per-user salt. Verification is constant-time.
    /// </summary>
    [DataContract]
    public class LocalUserStore
    {
        // Master switch. When false, no local login is allowed at all (even if AD
        // is down) UNLESS no AD is configured - see LoginService for that rule.
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; } = false;

        [DataMember(Name = "users")]
        public List<LocalUser> Users { get; set; } = new List<LocalUser>();

        // PBKDF2 parameters for NEW hashes. Existing users keep their own stored
        // iteration count, so this can be raised later without breaking old hashes.
        private const int HashIterations = 100_000;
        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        // ---------- queries ----------

        /// <summary>True if at least one user has the Admin role.</summary>
        public bool HasAdmin =>
            Users.Any(user => user.Role == UserRole.Admin);

        public bool Exists(string username) =>
            Users.Any(user => string.Equals(user.Username, (username ?? "").Trim(),
                                         StringComparison.OrdinalIgnoreCase));

        // ---------- create / update / delete ----------

        /// <summary>
        /// Adds a new user. Returns false if the username is blank or already exists.
        /// Does NOT save to disk - call Save() after a batch of changes.
        /// </summary>
        public bool AddUser(string username, string password, UserRole role)
        {
            username = (username ?? "").Trim();
            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrEmpty(password) ||
                Exists(username))
                return false;

            byte[] salt = NewSalt();
            byte[] hash = Hash(password, salt, HashIterations);

            Users.Add(new LocalUser
            {
                Username = username,
                RoleName = role.ToString(),
                Salt = Convert.ToBase64String(salt),
                Hash = Convert.ToBase64String(hash),
                Iterations = HashIterations
            });
            return true;
        }

        /// <summary>Changes an existing user's role. Returns false if not found.</summary>
        public bool SetRole(string username, UserRole role)
        {
            var user = Find(username);
            if (user == null) return false;
            user.RoleName = role.ToString();
            return true;
        }

        /// <summary>Resets an existing user's password. Returns false if not found.</summary>
        public bool SetPassword(string username, string newPassword)
        {
            var user = Find(username);
            if (user == null || string.IsNullOrEmpty(newPassword)) return false;

            byte[] salt = NewSalt();
            user.Salt = Convert.ToBase64String(salt);
            user.Hash = Convert.ToBase64String(Hash(newPassword, salt, HashIterations));
            user.Iterations = HashIterations;
            return true;
        }

        /// <summary>
        /// Removes a user. Returns false if not found, or if removing it would
        /// delete the last remaining Admin (which would lock everyone out of
        /// configuration). Guarding against that here keeps the UI simpler.
        /// </summary>
        public bool RemoveUser(string username)
        {
            var user = Find(username);
            if (user == null) return false;

            if (user.Role == UserRole.Admin &&
                Users.Count(u => u.Role == UserRole.Admin) == 1)
                return false;

            Users.Remove(user);
            return true;
        }

        // ---------- validation (used by the login fallback) ----------

        /// <summary>
        /// Validates a username/password against the stored hash. Returns an
        /// AuthenticatedUser on success, null otherwise. Constant-time comparison.
        /// </summary>
        public AuthenticatedUser Validate(string username, string password)
        {
            var user = Find(username);
            if (user == null || string.IsNullOrEmpty(password))
                return null;

            byte[] salt, expected;
            try
            {
                salt = Convert.FromBase64String(user.Salt);
                expected = Convert.FromBase64String(user.Hash);
            }
            catch
            {
                return null;
            }

            byte[] actual = Hash(password, salt, user.Iterations > 0 ? user.Iterations : HashIterations);

            if (!FixedTimeEquals(actual, expected))
                return null;

            return new AuthenticatedUser(user.Username, user.Role);
        }

        // ---------- helpers ----------

        private LocalUser Find(string username)
        {
            username = (username ?? "").Trim();
            return Users.FirstOrDefault(u =>
                string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        }

        private static byte[] NewSalt()
        {
            var salt = new byte[SaltBytes];
            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(salt);
            return salt;
        }

        private static byte[] Hash(string password, byte[] salt, int iterations)
        {
            // SHA-256 PBKDF2. (HashAlgorithmName overload exists on .NET 4.7.2.)
            using (var pbkdf2 = new Rfc2898DeriveBytes(
                password, salt, iterations, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(HashBytes);
            }
        }

        // Constant-time byte compare so verification time does not leak hash bytes.
        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        // ---------- cloning ----------

        /// <summary>
        /// Deep copy, so an editor (LocalUsersWindow) can work on a throwaway copy
        /// and discard it on Cancel without touching the live store.
        /// </summary>
        public LocalUserStore Clone()
        {
            var copy = new LocalUserStore { Enabled = this.Enabled };
            foreach (var u in Users)
            {
                copy.Users.Add(new LocalUser
                {
                    Username = u.Username,
                    RoleName = u.RoleName,
                    Hash = u.Hash,
                    Salt = u.Salt,
                    Iterations = u.Iterations
                });
            }
            return copy;
        }

        // ---------- persistence ----------

        // JSON file for storing local users
        private static string FilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "localusers.json");

        /// <summary>
        /// Loads from disk. Returns an empty store (disabled, no users) if the file
        /// is missing or unreadable - never throws.
        /// </summary>
        public static LocalUserStore Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new LocalUserStore();

                using (var fs = File.OpenRead(FilePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(LocalUserStore));
                    var store = (LocalUserStore)serializer.ReadObject(fs);
                    if (store == null) return new LocalUserStore();
                    if (store.Users == null) store.Users = new List<LocalUser>();
                    return store;
                }
            }
            catch
            {
                return new LocalUserStore();
            }
        }

        /// <summary>Saves to disk. Returns false if the write failed.</summary>
        public bool Save()
        {
            try
            {
                using (var ms = new MemoryStream())
                {
                    var serializer = new DataContractJsonSerializer(typeof(LocalUserStore));
                    serializer.WriteObject(ms, this);
                    File.WriteAllBytes(FilePath, ms.ToArray());
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}