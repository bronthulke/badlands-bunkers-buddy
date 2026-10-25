using System.Net;
using System.Text.RegularExpressions;
using BadlandsBunkers.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace BadlandsBunkers.Api;

public partial class BunkerFunctions
{
    private readonly BunkerStore _store;
    private readonly SessionService _sessions;

    public BunkerFunctions(BunkerStore store, SessionService sessions)
    {
        _store = store;
        _sessions = sessions;
    }

    [GeneratedRegex(@"^\d{1,8}$")]
    private static partial Regex CodePattern();

    private bool IsAuthorised(HttpRequestData req) =>
        _sessions.ValidateToken(req.ReadCookie(SessionService.CookieName));

    [Function("GetBunkers")]
    public async Task<HttpResponseData> GetBunkers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "bunkers")] HttpRequestData req)
    {
        if (!IsAuthorised(req)) return await Unauthorised(req);
        return await req.JsonAsync(await _store.GetBunkersAsync());
    }

    [Function("CreateBunker")]
    public async Task<HttpResponseData> CreateBunker(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "bunkers")] HttpRequestData req)
    {
        if (!IsAuthorised(req)) return await Unauthorised(req);

        var input = await req.ReadJsonAsync<BunkerInput>();
        if (input is null) return await req.ErrorAsync(HttpStatusCode.BadRequest, "Invalid body.");
        if (Validate(input) is { } error) return await req.ErrorAsync(HttpStatusCode.BadRequest, error);

        var bunker = await _store.CreateBunkerAsync(input);
        return await req.JsonAsync(bunker, HttpStatusCode.Created);
    }

    [Function("UpdateBunker")]
    public async Task<HttpResponseData> UpdateBunker(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "bunkers/{id}")] HttpRequestData req,
        string id)
    {
        if (!IsAuthorised(req)) return await Unauthorised(req);

        var input = await req.ReadJsonAsync<BunkerInput>();
        if (input is null) return await req.ErrorAsync(HttpStatusCode.BadRequest, "Invalid body.");
        if (Validate(input) is { } error) return await req.ErrorAsync(HttpStatusCode.BadRequest, error);

        var bunker = await _store.UpdateBunkerAsync(id, input);
        return bunker is null ? await NotFound(req) : await req.JsonAsync(bunker);
    }

    [Function("DeleteBunker")]
    public async Task<HttpResponseData> DeleteBunker(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "bunkers/{id}")] HttpRequestData req,
        string id)
    {
        if (!IsAuthorised(req)) return await Unauthorised(req);

        await _store.DeleteBunkerAsync(id);
        return req.Status(HttpStatusCode.NoContent);
    }

    [Function("SetBunkerActive")]
    public async Task<HttpResponseData> SetBunkerActive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "bunkers/{id}/active/{state}")] HttpRequestData req,
        string id, string state)
    {
        if (!IsAuthorised(req)) return await Unauthorised(req);

        var active = state.Equals("on", StringComparison.OrdinalIgnoreCase);
        var bunker = await _store.SetActiveAsync(id, active);
        return bunker is null ? await NotFound(req) : await req.JsonAsync(bunker);
    }

    [Function("SetBunkerCode")]
    public async Task<HttpResponseData> SetBunkerCode(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "bunkers/{id}/code")] HttpRequestData req,
        string id)
    {
        if (!IsAuthorised(req)) return await Unauthorised(req);

        var input = await req.ReadJsonAsync<CodeInput>();
        if (input is null) return await req.ErrorAsync(HttpStatusCode.BadRequest, "Invalid body.");

        var code = input.Code?.Trim();
        if (!string.IsNullOrEmpty(code) && !CodePattern().IsMatch(code))
            return await req.ErrorAsync(HttpStatusCode.BadRequest, "Code must be digits only.");

        var bunker = await _store.SetCodeAsync(id, code);
        return bunker is null ? await NotFound(req) : await req.JsonAsync(bunker);
    }

    [Function("ClearNight")]
    public async Task<HttpResponseData> ClearNight(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "night/clear")] HttpRequestData req)
    {
        if (!IsAuthorised(req)) return await Unauthorised(req);
        return await req.JsonAsync(await _store.ClearNightAsync());
    }

    [Function("GetNights")]
    public async Task<HttpResponseData> GetNights(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "nights")] HttpRequestData req)
    {
        if (!IsAuthorised(req)) return await Unauthorised(req);
        return await req.JsonAsync(await _store.GetNightsAsync());
    }

    internal static string? Validate(BunkerInput input)
    {
        if (input.X is < 0 or > 9999) return "X must be between 0 and 9999.";
        if (input.Y is < 0 or > 9999) return "Y must be between 0 and 9999.";
        if (input.Description.Length > 500) return "Description is too long.";
        return null;
    }

    private static Task<HttpResponseData> Unauthorised(HttpRequestData req) =>
        req.ErrorAsync(HttpStatusCode.Unauthorized, "Not signed in.");

    private static Task<HttpResponseData> NotFound(HttpRequestData req) =>
        req.ErrorAsync(HttpStatusCode.NotFound, "Bunker not found.");
}
