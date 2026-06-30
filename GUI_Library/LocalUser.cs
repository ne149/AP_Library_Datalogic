// ===================== LocalUser.cs =====================
using System;
using System.Runtime.Serialization;

namespace GUI_Library
{
    /// <summary>
    /// A single local user. Used as fallback authentication when AD is unreachable
    /// (or as the only login when no AD is configured yet).
    ///
    /// The password is NEVER stored in clear text - only a PBKDF2 hash plus a
    /// per-user random salt. The role is stored as the enum NAME (string), not its
    /// numeric value, so reordering or inserting roles in the UserRole enum later
    /// (e.g. going from 3 to 5 roles) does not silently change existing users' roles.
    /// </summary>
    [DataContract]
    public class LocalUser // Used to store info in JSON-file
    {
        [DataMember(Name = "username")]
        public string Username { get; set; } = "";

        // Role stored as the enum name, e.g. "Engineer". See class summary for why.
        [DataMember(Name = "role")]
        public string RoleName { get; set; } = "";

        // Base64 of the PBKDF2-derived hash.
        [DataMember(Name = "hash")]
        public string Hash { get; set; } = "";

        // Base64 of the per-user random salt.
        [DataMember(Name = "salt")]
        public string Salt { get; set; } = "";

        // PBKDF2 iteration count used when this hash was created. Stored per-user
        // so the count can be raised in future without invalidating old hashes.
        [DataMember(Name = "iterations")]
        public int Iterations { get; set; }

        /// <summary>
        /// Parses RoleName back to a UserRole, and writes it back as the enum name
        /// when set. Falls back to Operator (least privilege) if the stored name no
        /// longer matches any enum value. The setter lets the management UI bind a
        /// role dropdown directly to this property.
        /// </summary>
        public UserRole Role
        {
            get
            {
                if (Enum.TryParse(RoleName, out UserRole r))
                    return r;
                return UserRole.Operator;
            }
            set
            {
                RoleName = value.ToString();
            }
        }
    }
}