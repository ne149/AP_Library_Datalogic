// ===================== LocalUserService.cs =====================
namespace GUI_Library
{
    /// <summary>
    /// IUserService backed by the local user store. Replaces the old hardcoded
    /// test implementation. Validates against PBKDF2 hashes - no clear-text
    /// passwords anywhere.
    /// This service does NOT decide WHEN local login is allowed (AD down vs. up,
    /// enabled vs. disabled) - that policy lives in LoginService. 
    /// 
    /// This class only answers "are these credentials valid for a local user?".
    /// </summary>
    public class LocalUserService : IUserService
    {
        private readonly LocalUserStore _store;

        public LocalUserService(LocalUserStore store)
        {
            _store = store;
        }

        public AuthenticatedUser Login(string username, string password)
        {
            if (_store == null) return null;
            return _store.Validate(username, password);
        }
    }
}