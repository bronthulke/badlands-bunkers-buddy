using BadlandsBunkers.Api;

namespace BadlandsBunkers.Tests;

/// <summary>
/// This is the only thing standing between the bunker list and the rest of the server,
/// so it gets the most scrutiny.
/// </summary>
public class SessionServiceTests
{
    private const string Password = "hunter2";
    private const string SigningKey = "signing-key-for-tests-0123456789";

    private static SessionService Create(string? password = null, string? key = null, int days = 30) =>
        new(password ?? Password, key ?? SigningKey, days);

    // ---- password checking ----

    [Fact]
    public void PasswordMatches_accepts_the_exact_password()
    {
        Assert.True(Create().PasswordMatches(Password));
    }

    [Theory]
    [InlineData("hunter3")]       // one character off
    [InlineData("hunter")]        // a prefix
    [InlineData("hunter22")]      // the password plus junk
    [InlineData("HUNTER2")]       // wrong case
    [InlineData(" hunter2")]      // leading whitespace
    [InlineData("hunter2 ")]      // trailing whitespace
    [InlineData("")]
    [InlineData(null)]
    public void PasswordMatches_rejects_everything_else(string? candidate)
    {
        Assert.False(Create().PasswordMatches(candidate));
    }

    [Fact]
    public void PasswordMatches_rejects_everything_when_no_password_is_configured()
    {
        // A misconfigured deployment must fail closed, not let the whole group's data out.
        var service = Create(password: "");

        Assert.False(service.PasswordMatches(""));
        Assert.False(service.PasswordMatches("anything"));
        Assert.False(service.PasswordMatches(null));
    }

    // ---- token round-tripping ----

    [Fact]
    public void A_freshly_issued_token_validates()
    {
        var service = Create();

        Assert.True(service.ValidateToken(service.IssueToken()));
    }

    [Fact]
    public void Tokens_issued_by_a_different_signing_key_are_rejected()
    {
        // This is the "rotate the key to kick everyone out" behaviour.
        var token = Create(key: "the-original-signing-key-aaaaaaa").IssueToken();
        var afterRotation = Create(key: "a-completely-different-key-bbbb");

        Assert.False(afterRotation.ValidateToken(token));
    }

    [Fact]
    public void An_expired_token_is_rejected()
    {
        var alreadyExpired = Create(days: -1);

        Assert.False(alreadyExpired.ValidateToken(alreadyExpired.IssueToken()));
    }

    [Fact]
    public void Extending_the_expiry_without_resigning_is_rejected()
    {
        // The attack this blocks: grab a valid cookie, push the expiry out ten years.
        var service = Create();
        var signature = service.IssueToken().Split('.')[1];
        var farFuture = DateTimeOffset.UtcNow.AddYears(10).ToUnixTimeSeconds();

        Assert.False(service.ValidateToken($"{farFuture}.{signature}"));
    }

    [Fact]
    public void A_tampered_signature_is_rejected()
    {
        var service = Create();
        var parts = service.IssueToken().Split('.');
        var tampered = parts[1][..^1] + (parts[1][^1] == 'A' ? 'B' : 'A');

        Assert.False(service.ValidateToken($"{parts[0]}.{tampered}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    [InlineData("no-dot-separator")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("1.2.3")]                      // too many segments
    [InlineData("notanumber.signature")]       // unparseable expiry
    [InlineData("99999999999999999999.sig")]   // expiry that overflows long
    [InlineData("1700000000.")]                // empty signature
    public void Malformed_tokens_are_rejected_without_throwing(string? token)
    {
        Assert.False(Create().ValidateToken(token));
    }

    [Fact]
    public void Two_tokens_signed_for_the_same_expiry_match()
    {
        // Sanity check that signing is deterministic — otherwise nothing would ever validate.
        var a = Create();
        var b = Create();
        var token = a.IssueToken();

        Assert.True(b.ValidateToken(token));
    }

    // ---- cookie construction ----

    [Fact]
    public void The_session_cookie_carries_the_flags_that_make_it_safe()
    {
        var service = Create();
        var cookie = service.BuildSetCookie(service.IssueToken());

        Assert.StartsWith($"{SessionService.CookieName}=", cookie);
        Assert.Contains("HttpOnly", cookie);          // unreadable from JS
        Assert.Contains("Secure", cookie);            // HTTPS only
        Assert.Contains("SameSite=Strict", cookie);   // no cross-site sends
        Assert.Contains("Path=/", cookie);
    }

    [Fact]
    public void The_cookie_lifetime_follows_the_configured_session_length()
    {
        var service = Create(days: 7);

        Assert.Contains($"Max-Age={7 * 24 * 60 * 60}", service.BuildSetCookie(service.IssueToken()));
    }

    [Fact]
    public void Clearing_the_cookie_expires_it_immediately()
    {
        var cookie = Create().BuildClearCookie();

        Assert.Contains("Max-Age=0", cookie);
        Assert.Contains($"{SessionService.CookieName}=;", cookie);
    }

    [Fact]
    public void The_token_never_contains_the_cookie_delimiters()
    {
        // Base64 '+' and '/' would survive, but ';' or ',' would split the Set-Cookie header.
        for (var i = 0; i < 50; i++)
        {
            var token = Create(key: Guid.NewGuid().ToString()).IssueToken();

            Assert.DoesNotContain(';', token);
            Assert.DoesNotContain(',', token);
            Assert.DoesNotContain(' ', token);
            Assert.DoesNotContain('=', token);
        }
    }
}
