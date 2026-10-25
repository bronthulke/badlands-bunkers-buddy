using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace BadlandsBunkers.Client.Services;

/// <summary>Makes the browser send our session cookie with every API call.</summary>
public class CredentialsHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}
