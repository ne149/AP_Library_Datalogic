// ===================== IUserService.cs =====================
namespace GUI_Library
{
    /// <summary>
    /// Login-graensefladen. Resten af appen kender KUN denne.
    /// Senere skiftes LocalUserService ud med en AD/MSAL-implementering -
    /// alt andet (roller, rettigheder, UI-spaerring, audit) er uaendret.
    /// </summary>
    public interface IUserService
    {
        /// <returns>AuthenticatedUser ved succes, ellers null.</returns>
        AuthenticatedUser Login(string username, string password);
    }
}