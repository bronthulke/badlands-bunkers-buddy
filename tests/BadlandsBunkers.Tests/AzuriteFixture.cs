using System.Net.Sockets;
using BadlandsBunkers.Api;
using Xunit;

namespace BadlandsBunkers.Tests;

/// <summary>
/// Storage tests run against Azurite. If it isn't running they're skipped rather than failed,
/// so a plain `dotnet test` stays green on a machine without the emulator. Start it with:
///   azurite-table --tablePort 10002
/// </summary>
public static class Azurite
{
    public const string ConnectionString = "UseDevelopmentStorage=true";

    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("127.0.0.1", 10002).Wait(TimeSpan.FromSeconds(2))
                   && client.Connected;
        }
        catch
        {
            return false;
        }
    });

    public static bool IsRunning => Available.Value;
}

/// <summary>A Fact that quietly skips when the storage emulator isn't up.</summary>
public sealed class StorageFactAttribute : FactAttribute
{
    public StorageFactAttribute()
    {
        if (!Azurite.IsRunning)
            Skip = "Azurite is not running on port 10002.";
    }
}

/// <summary>Gives each test class its own pair of throwaway tables.</summary>
public class StoreFixture : IAsyncLifetime
{
    public BunkerStore Store { get; private set; } = null!;

    private readonly string _prefix = "t" + Guid.NewGuid().ToString("n")[..12];

    public Task InitializeAsync()
    {
        Store = new BunkerStore(Azurite.ConnectionString, _prefix);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (!Azurite.IsRunning) return;

        try
        {
            await Store.DeleteTablesAsync();
        }
        catch
        {
            // Nothing was ever created — fine.
        }
    }
}
