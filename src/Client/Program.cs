using BadlandsBunkers.Client;
using BadlandsBunkers.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddTransient<CredentialsHandler>();
builder.Services.AddHttpClient("api", client =>
        client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<CredentialsHandler>();

// Deliberately scoped, not a typed client: typed clients are transient, which would hand
// every component its own BunkerApi. The layout listens for SessionLost to swap in the
// login screen, so every component has to be talking to the same instance.
builder.Services.AddScoped(sp =>
    new BunkerApi(sp.GetRequiredService<IHttpClientFactory>().CreateClient("api")));

await builder.Build().RunAsync();
