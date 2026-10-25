using System.Net;
using BadlandsBunkers.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace BadlandsBunkers.Api;

public class AuthFunctions
{
    private readonly SessionService _sessions;

    public AuthFunctions(SessionService sessions) => _sessions = sessions;

    [Function("Login")]
    public async Task<HttpResponseData> Login(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "login")] HttpRequestData req)
    {
        var input = await req.ReadJsonAsync<LoginInput>();
        if (input is null || !_sessions.PasswordMatches(input.Password))
        {
            // Slow failed attempts down a little — this is the one endpoint worth guessing at.
            await Task.Delay(500);
            return await req.ErrorAsync(HttpStatusCode.Unauthorized, "Wrong password.");
        }

        var response = await req.JsonAsync(new SessionInfo { Authenticated = true });
        response.Headers.Add("Set-Cookie", _sessions.BuildSetCookie(_sessions.IssueToken()));
        return response;
    }

    [Function("Logout")]
    public async Task<HttpResponseData> Logout(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "logout")] HttpRequestData req)
    {
        var response = await req.JsonAsync(new SessionInfo { Authenticated = false });
        response.Headers.Add("Set-Cookie", _sessions.BuildClearCookie());
        return response;
    }

    [Function("Session")]
    public async Task<HttpResponseData> Session(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "session")] HttpRequestData req)
    {
        var authenticated = _sessions.ValidateToken(req.ReadCookie(SessionService.CookieName));
        return await req.JsonAsync(new SessionInfo { Authenticated = authenticated });
    }
}
