using BadlandsBunkers.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        var connectionString = config["TableStorageConnectionString"]
            ?? throw new InvalidOperationException("TableStorageConnectionString is not configured.");
        services.AddSingleton(new BunkerStore(connectionString));

        var password = config["SharedPassword"]
            ?? throw new InvalidOperationException("SharedPassword is not configured.");
        var signingKey = config["AuthSigningKey"]
            ?? throw new InvalidOperationException("AuthSigningKey is not configured.");
        var sessionDays = int.TryParse(config["SessionDays"], out var days) ? days : 30;
        services.AddSingleton(new SessionService(password, signingKey, sessionDays));
    })
    .Build();

host.Run();
