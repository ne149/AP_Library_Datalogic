// ===================== IUserService.cs =====================
namespace GUI_Library
{
    /// <summary>
    /// The login interface. The rest of the app knows ONLY this one.
    /// Later, LocalUserService is swapped out for an AD/MSAL implementation -
    /// everything else (roles, permissions, UI blocking, audit) stays unchanged.
    /// </summary>
    public interface IUserService
    {
        /// <returns>AuthenticatedUser on success, otherwise null.</returns>
        AuthenticatedUser Login(string username, string password);
    }
}