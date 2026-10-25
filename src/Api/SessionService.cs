using System.Security.Cryptography;
using System.Text;

namespace BadlandsBunkers.Api;

/// <summary>
/// Shared-password auth. One password for the whole group; on success we hand back an HttpOnly
/// cookie containing an expiry plus an HMAC of that expiry. There's no user identity to track —
/// the cookie only proves "whoever holds this knew the password", which is exactly the bar we want.
/// </summary>
public class SessionService
{
    public const string CookieName = "bb_session";

    private readonly byte[] _signingKey;
    private readonly string _password;
    private readonly TimeSpan _lifetime;

    public SessionService(string password, string signingKey, int sessionDays)
    {
        _password = password;
        _signingKey = Encoding.UTF8.GetBytes(signingKey);
        _lifetime = TimeSpan.FromDays(sessionDays);
    }

    public bool PasswordMatches(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(_password)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate),
            Encoding.UTF8.GetBytes(_password));
    }

    public string IssueToken()
    {
        var expires = DateTimeOffset.UtcNow.Add(_lifetime).ToUnixTimeSeconds();
        return $"{expires}.{Sign(expires)}";
    }

    public bool ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;

        var parts = token.Split('.');
        if (parts.Length != 2) return false;
        if (!long.TryParse(parts[0], out var expires)) return false;
        if (DateTimeOffset.FromUnixTimeSeconds(expires) <= DateTimeOffset.UtcNow) return false;

        var expected = Encoding.UTF8.GetBytes(Sign(expires));
        var actual = Encoding.UTF8.GetBytes(parts[1]);
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public string BuildSetCookie(string token) =>
        $"{CookieName}={token}; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age={(int)_lifetime.TotalSeconds}";

    public string BuildClearCookie() =>
        $"{CookieName}=; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age=0";

    private string Sign(long expires)
    {
        using var hmac = new HMACSHA256(_signingKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(expires.ToString()));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
