// ===================== LoginService.cs =====================
namespace GUI_Library
{
    /// <summary>
    /// Decides HOW a login is attempted, following three well-defined states.
    /// This is the single place that encodes the fallback policy, so there are no
    /// hidden back doors:
    ///
    ///   1. No AD configured        -> local users are the ONLY way in.
    ///                                 (Lets you run/test without any AD server.)
    ///   2. AD configured, local OFF -> AD only. No local path in, even if AD is down.
    ///   3. AD configured, local ON  -> AD first. Local users are tried ONLY if AD
    ///                                 is UNREACHABLE - never when AD is reachable
    ///                                 but the password was simply wrong.
    ///
    /// The wrong-password-vs-server-down distinction is critical: if a wrong AD
    /// password fell through to local login, anyone could reach the local accounts
    /// by deliberately giving AD a bad password. So fallback fires only on a real
    /// connection failure.
    /// </summary>
    public class LoginService : IUserService
    {
        private readonly AdSettings _adSettings;
        private readonly LocalUserStore _localStore;

        public LoginService(AdSettings adSettings, LocalUserStore localStore)
        {
            _adSettings = adSettings;
            _localStore = localStore;
        }

        /// <summary>
        /// True when the most recent Login attempt actually authenticated via a
        /// local user (so the caller can label the audit entry as a local login).
        /// </summary>
        public bool LastLoginWasLocal { get; private set; }

        public AuthenticatedUser Login(string username, string password)
        {
            LastLoginWasLocal = false;

            bool adConfigured = _adSettings != null && _adSettings.IsConfigured;

            // ----- State 1: no AD configured -> local only -----
            if (!adConfigured)
                return TryLocal(username, password);

            // AD is configured. Build the AD service from settings.
            var ad = new AdAuthenticator(
                _adSettings.Server,
                _adSettings.Domain,
                _adSettings.GroupOperator,
                _adSettings.GroupEngineer,
                _adSettings.GroupAdmin,
                _adSettings.Port,
                _adSettings.UseLdaps);

            // Try AD. TryLogin distinguishes "reachable" from "unreachable".
            var result = ad.TryLogin(username, password, out bool reachable);

            // AD reachable: AD is authoritative. Whatever it said (success or
            // bad-credentials) stands. NO local fallback here.
            if (reachable)
                return result;

            // ----- AD unreachable -----
            // State 2: local disabled -> no way in. Return null.
            // State 3: local enabled  -> fall back to local users.
            if (_localStore != null && _localStore.Enabled)
                return TryLocal(username, password);

            return null;
        }

        private AuthenticatedUser TryLocal(string username, string password)
        {
            var user = _localStore?.Validate(username, password);
            LastLoginWasLocal = user != null;
            return user;
        }
    }
}