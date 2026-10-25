using System.Net;
using BadlandsBunkers.Api;
using Microsoft.Azure.Functions.Worker.Http;

namespace BadlandsBunkers.Tests;

/// <summary>
/// Every data endpoint must refuse an unauthenticated caller. Testing SessionService in
/// isolation doesn't prove the functions actually consult it — this does, endpoint by endpoint,
/// so a new route can't quietly ship without a gate.
/// </summary>
public class EndpointAuthTests
{
    private const string SigningKey = "endpoint-auth-test-key-00000000";

    private static readonly SessionService Sessions = new("pw", SigningKey, 30);

    private static BunkerFunctions Functions() =>
        new(new BunkerStore(Azurite.ConnectionString, "unused"), Sessions);

    /// <summary>Every guarded endpoint, as a callable paired with a readable name.</summary>
    public static TheoryData<string> GuardedEndpoints() => new()
    {
        "GetBunkers", "CreateBunker", "UpdateBunker", "DeleteBunker",
        "SetBunkerActive", "SetBunkerCode", "ClearNight", "GetNights"
    };

    private static Task<HttpResponseData> InvokeAsync(string name, FakeHttpRequestData req)
    {
        var f = Functions();
        return name switch
        {
            "GetBunkers" => f.GetBunkers(req),
            "CreateBunker" => f.CreateBunker(req),
            "UpdateBunker" => f.UpdateBunker(req, "some-id"),
            "DeleteBunker" => f.DeleteBunker(req, "some-id"),
            "SetBunkerActive" => f.SetBunkerActive(req, "some-id", "on"),
            "SetBunkerCode" => f.SetBunkerCode(req, "some-id"),
            "ClearNight" => f.ClearNight(req),
            "GetNights" => f.GetNights(req),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unmapped endpoint.")
        };
    }

    [Theory]
    [MemberData(nameof(GuardedEndpoints))]
    public async Task Rejects_a_caller_with_no_cookie(string endpoint)
    {
        var response = await InvokeAsync(endpoint, new FakeHttpRequestData());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(GuardedEndpoints))]
    public async Task Rejects_a_caller_with_a_forged_cookie(string endpoint)
    {
        var forged = new SessionService("pw", "a-totally-different-key-99999", 30).IssueToken();

        var response = await InvokeAsync(endpoint, new FakeHttpRequestData($"bb_session={forged}"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(GuardedEndpoints))]
    public async Task Rejects_a_caller_with_an_expired_cookie(string endpoint)
    {
        var stale = new SessionService("pw", SigningKey, -1).IssueToken();

        var response = await InvokeAsync(endpoint, new FakeHttpRequestData($"bb_session={stale}"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(GuardedEndpoints))]
    public async Task Rejects_a_caller_whose_cookie_is_under_the_wrong_name(string endpoint)
    {
        var valid = Sessions.IssueToken();

        var response = await InvokeAsync(endpoint, new FakeHttpRequestData($"some_other_cookie={valid}"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [StorageFact]
    public async Task A_valid_cookie_gets_through_to_the_data()
    {
        // The mirror of the above: proves the gate isn't simply rejecting everything.
        var store = new BunkerStore(Azurite.ConnectionString, "t" + Guid.NewGuid().ToString("n")[..12]);
        try
        {
            var functions = new BunkerFunctions(store, Sessions);
            var req = new FakeHttpRequestData($"bb_session={Sessions.IssueToken()}");

            var response = await functions.GetBunkers(req);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await store.DeleteTablesAsync();
        }
    }
}

public class LoginEndpointTests
{
    private static AuthFunctions Functions() =>
        new(new SessionService("correct-horse", "login-test-signing-key-000000", 30));

    [Fact]
    public async Task The_wrong_password_is_refused_and_sets_no_cookie()
    {
        var response = await Functions().Login(
            new FakeHttpRequestData(body: """{"password":"wrong"}"""));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task An_empty_body_is_refused()
    {
        var response = await Functions().Login(new FakeHttpRequestData(body: ""));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_right_password_issues_a_usable_session_cookie()
    {
        var sessions = new SessionService("correct-horse", "login-test-signing-key-000000", 30);

        var response = await new AuthFunctions(sessions).Login(
            new FakeHttpRequestData(body: """{"password":"correct-horse"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("HttpOnly", cookie);

        // The cookie it hands out must be one the gate will subsequently accept.
        var token = cookie.Split(';')[0]["bb_session=".Length..];
        Assert.True(sessions.ValidateToken(token));
    }

    [Fact]
    public async Task Signing_out_clears_the_cookie()
    {
        var response = await Functions().Logout(new FakeHttpRequestData());

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("Max-Age=0", cookie);
    }

    [Fact]
    public async Task The_session_check_reports_false_for_an_anonymous_caller()
    {
        var response = (FakeHttpResponseData)await Functions().Session(new FakeHttpRequestData());

        Assert.Contains("\"authenticated\":false", response.ReadBodyAsString());
    }

    [Fact]
    public async Task The_session_check_reports_true_for_a_signed_in_caller()
    {
        var sessions = new SessionService("correct-horse", "login-test-signing-key-000000", 30);
        var req = new FakeHttpRequestData($"bb_session={sessions.IssueToken()}");

        var response = (FakeHttpResponseData)await new AuthFunctions(sessions).Session(req);

        Assert.Contains("\"authenticated\":true", response.ReadBodyAsString());
    }
}
