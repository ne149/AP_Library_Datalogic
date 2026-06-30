// ===================== UserRole.cs =====================
namespace GUI_Library
{
    /// <summary>
    /// The roles and their permissions. This is where a role is assigned permissions.
    /// These three roles match the AD group mapping in AdAuthenticator.MapGroupsToPermissions,
    /// so a local user and an AD user with the same role get identical permissions.
    /// Add a new role by adding an enum value and a case in Permissions().
    /// </summary>
    public enum UserRole
    {
        Operator,   // operate + gauge
        Engineer,   // operate + blob
        Admin       // full access
    }

    public static class UserRoleExtensions
    {
        public static Permission Permissions(this UserRole role)
        {
            switch (role)
            {
                case UserRole.Operator:
                    return Permission.CanOperate
                         | Permission.CanEditGauge
                         | Permission.CanSaveProgram;

                case UserRole.Engineer:
                    return Permission.CanOperate
                         | Permission.CanEditBlob
                         | Permission.CanSaveProgram;

                case UserRole.Admin:
                    return Permission.CanOperate
                         | Permission.CanEditGauge
                         | Permission.CanEditBlob
                         | Permission.CanSaveProgram
                         | Permission.CanViewAudit;

                default:
                    return Permission.None;
            }
        }

        // Friendly text for UI/audit.
        public static string DisplayName(this UserRole role)
        {
            switch (role)
            {
                case UserRole.Operator: return "Operator";
                case UserRole.Engineer: return "Engineer";
                case UserRole.Admin: return "Admin";
                default: return role.ToString();
            }
        }
    }
}