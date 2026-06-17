// ===================== UserRole.cs =====================
namespace GUI_Library
{
    /// <summary>
    /// Rollerne og deres rettigheder. Dette er det ENESTE sted rolle->rettigheds-
    /// kortlaegningen defineres. Tilfoej en ny rolle ved at tilfoeje en enum-vaerdi
    /// og en case i Permissions().
    /// </summary>
    public enum UserRole
    {
        Operator,
        Tekniker,
        VisionMand,
        Admin
    }

    public static class UserRoleExtensions
    {
        public static Permission Permissions(this UserRole role)
        {
            switch (role)
            {
                case UserRole.Operator:
                    return Permission.CanOperate;

                case UserRole.Tekniker:
                    return Permission.CanOperate
                         | Permission.CanEditGauge
                         | Permission.CanSaveProgram;

                case UserRole.VisionMand:
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

        // Paen tekst til UI/audit (saa vi ikke viser "VisionMand" raat hvis vi vil aendre det).
        public static string DisplayName(this UserRole role)
        {
            switch (role)
            {
                case UserRole.Operator: return "Operatoer";
                case UserRole.Tekniker: return "Tekniker";
                case UserRole.VisionMand: return "Vision";
                case UserRole.Admin: return "Admin";
                default: return role.ToString();
            }
        }
    }
}