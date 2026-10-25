using System.Net;
using System.Net.Http.Json;
using BadlandsBunkers.Shared;

namespace BadlandsBunkers.Client.Services;

/// <summary>Raised when the API tells us the session has gone, so the shell can bounce to login.</summary>
public class SessionExpiredException : Exception
{
    public SessionExpiredException() : base("Session expired.") { }
}

public class BunkerApi
{
    private readonly HttpClient _http;

    public BunkerApi(HttpClient http) => _http = http;

    public event Action? SessionLost;

    public async Task<bool> LoginAsync(string password)
    {
        var response = await _http.PostAsJsonAsync("api/login", new LoginInput { Password = password });
        return response.IsSuccessStatusCode;
    }

    public async Task LogoutAsync() => await _http.PostAsync("api/logout", null);

    public async Task<bool> IsAuthenticatedAsync()
    {
        try
        {
            var info = await _http.GetFromJsonAsync<SessionInfo>("api/session");
            return info?.Authenticated ?? false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public Task<List<Bunker>> GetBunkersAsync() =>
        SendAsync<List<Bunker>>(() => _http.GetAsync("api/bunkers"))!;

    public Task<List<NightRecord>> GetNightsAsync() =>
        SendAsync<List<NightRecord>>(() => _http.GetAsync("api/nights"))!;

    public Task<Bunker?> CreateBunkerAsync(BunkerInput input) =>
        SendAsync<Bunker>(() => _http.PostAsJsonAsync("api/bunkers", input));

    public Task<Bunker?> UpdateBunkerAsync(string id, BunkerInput input) =>
        SendAsync<Bunker>(() => _http.PutAsJsonAsync($"api/bunkers/{id}", input));

    public async Task DeleteBunkerAsync(string id) =>
        await SendAsync(() => _http.DeleteAsync($"api/bunkers/{id}"));

    public Task<Bunker?> SetActiveAsync(string id, bool active) =>
        SendAsync<Bunker>(() => _http.PostAsync($"api/bunkers/{id}/active/{(active ? "on" : "off")}", null));

    public Task<Bunker?> SetCodeAsync(string id, string? code) =>
        SendAsync<Bunker>(() => _http.PostAsJsonAsync($"api/bunkers/{id}/code", new CodeInput { Code = code }));

    public Task<NightRecord?> ClearNightAsync() =>
        SendAsync<NightRecord>(() => _http.PostAsync("api/night/clear", null));

    private async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        var response = await send();
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            SessionLost?.Invoke();
            throw new SessionExpiredException();
        }

        if (!response.IsSuccessStatusCode)
        {
            var problem = await TryReadErrorAsync(response);
            throw new ApplicationException(problem ?? $"Request failed ({(int)response.StatusCode}).");
        }

        return response;
    }

    private async Task<T?> SendAsync<T>(Func<Task<HttpResponseMessage>> send)
    {
        var response = await SendAsync(send);
        if (response.StatusCode == HttpStatusCode.NoContent) return default;
        return await response.Content.ReadFromJsonAsync<T>();
    }

    private static async Task<string?> TryReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            return payload is not null && payload.TryGetValue("error", out var message) ? message : null;
        }
        catch
        {
            return null;
        }
    }
}
