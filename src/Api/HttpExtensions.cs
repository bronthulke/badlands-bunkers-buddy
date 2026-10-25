using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;

namespace BadlandsBunkers.Api;

public static class HttpExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string? ReadCookie(this HttpRequestData req, string name)
    {
        if (!req.Headers.TryGetValues("Cookie", out var headers)) return null;

        foreach (var header in headers)
        {
            foreach (var pair in header.Split(';'))
            {
                var trimmed = pair.Trim();
                if (trimmed.StartsWith(name + "=", StringComparison.Ordinal))
                    return trimmed[(name.Length + 1)..];
            }
        }

        return null;
    }

    public static async Task<T?> ReadJsonAsync<T>(this HttpRequestData req)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(req.Body, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public static async Task<HttpResponseData> JsonAsync<T>(
        this HttpRequestData req, T body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = req.CreateResponse(status);
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonOptions));
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        return response;
    }

    public static HttpResponseData Status(this HttpRequestData req, HttpStatusCode status) =>
        req.CreateResponse(status);

    public static async Task<HttpResponseData> ErrorAsync(
        this HttpRequestData req, HttpStatusCode status, string message) =>
        await req.JsonAsync(new { error = message }, status);
}
