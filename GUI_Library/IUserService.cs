// ===================== IUserService.cs =====================
namespace GUI_Library
{
    /// <summary>
    /// The login interface. The rest of the app knows ONLY this one.
    /// LocalUserService uses UserRole, AdUserService uses Permission flags directly -
    /// everything else (permissions, UI blocking, audit) stays unchanged.
    /// </summary>
    public interface IUserService
    {
        /// <returns>AuthenticatedUser on success, otherwise null.</returns>
        AuthenticatedUser Login(string username, string password);
    }
}