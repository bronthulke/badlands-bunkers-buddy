using Azure;
using Azure.Data.Tables;
using BadlandsBunkers.Shared;

namespace BadlandsBunkers.Api;

/// <summary>Azure Table Storage persistence. Everything lives in one partition — we're tracking
/// a few dozen rows, so a partition scan is the whole dataset and costs nothing.</summary>
public class BunkerStore
{
    private const string BunkerPartition = "bunker";
    private const string NightPartition = "night";

    private readonly TableClient _bunkers;
    private readonly TableClient _nights;
    private bool _initialised;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    /// <param name="tablePrefix">Lets tests point at throwaway tables instead of the real ones.</param>
    public BunkerStore(string connectionString, string tablePrefix = "")
    {
        _bunkers = new TableClient(connectionString, $"{tablePrefix}Bunkers");
        _nights = new TableClient(connectionString, $"{tablePrefix}Nights");
    }

    /// <summary>Drops both tables. Test cleanup only — never call this against real data.</summary>
    internal async Task DeleteTablesAsync()
    {
        await _bunkers.DeleteAsync();
        await _nights.DeleteAsync();
    }

    private async Task EnsureTablesAsync()
    {
        if (_initialised) return;
        await _initLock.WaitAsync();
        try
        {
            if (_initialised) return;
            await _bunkers.CreateIfNotExistsAsync();
            await _nights.CreateIfNotExistsAsync();
            _initialised = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<List<Bunker>> GetBunkersAsync()
    {
        await EnsureTablesAsync();
        var results = new List<Bunker>();
        await foreach (var entity in _bunkers.QueryAsync<TableEntity>(e => e.PartitionKey == BunkerPartition))
            results.Add(ToBunker(entity));

        // Active first, then by coordinate so the list reads like a map sweep.
        return results
            .OrderByDescending(b => b.IsActive)
            .ThenBy(b => b.X)
            .ThenBy(b => b.Y)
            .ToList();
    }

    public async Task<Bunker?> GetBunkerAsync(string id)
    {
        await EnsureTablesAsync();
        try
        {
            var response = await _bunkers.GetEntityAsync<TableEntity>(BunkerPartition, id);
            return ToBunker(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<Bunker> CreateBunkerAsync(BunkerInput input)
    {
        await EnsureTablesAsync();
        var bunker = new Bunker
        {
            Id = Guid.NewGuid().ToString("n"),
            X = input.X,
            Y = input.Y,
            Description = input.Description.Trim(),
            CreatedUtc = DateTimeOffset.UtcNow
        };
        await _bunkers.AddEntityAsync(ToEntity(bunker));
        return bunker;
    }

    public async Task<Bunker?> UpdateBunkerAsync(string id, BunkerInput input)
    {
        var bunker = await GetBunkerAsync(id);
        if (bunker is null) return null;

        bunker.X = input.X;
        bunker.Y = input.Y;
        bunker.Description = input.Description.Trim();
        await _bunkers.UpdateEntityAsync(ToEntity(bunker), ETag.All, TableUpdateMode.Replace);
        return bunker;
    }

    public async Task<bool> DeleteBunkerAsync(string id)
    {
        await EnsureTablesAsync();
        var response = await _bunkers.DeleteEntityAsync(BunkerPartition, id);
        return !response.IsError;
    }

    /// <summary>Flip a bunker in or out of tonight's active set. Deactivating also drops its code.</summary>
    public async Task<Bunker?> SetActiveAsync(string id, bool active)
    {
        var bunker = await GetBunkerAsync(id);
        if (bunker is null) return null;
        if (bunker.IsActive == active) return bunker;

        bunker.IsActive = active;
        if (active)
        {
            bunker.ActivatedUtc = DateTimeOffset.UtcNow;
            bunker.LastActiveUtc = bunker.ActivatedUtc;
            bunker.ActiveCount++;
        }
        else
        {
            bunker.ActivatedUtc = null;
            bunker.Code = null;
        }

        await _bunkers.UpdateEntityAsync(ToEntity(bunker), ETag.All, TableUpdateMode.Replace);
        return bunker;
    }

    /// <summary>Set (or clear) tonight's lock code. Setting a code implies the bunker is active.</summary>
    public async Task<Bunker?> SetCodeAsync(string id, string? code)
    {
        var bunker = await GetBunkerAsync(id);
        if (bunker is null) return null;

        code = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        bunker.Code = code;

        if (code is not null && !bunker.IsActive)
        {
            bunker.IsActive = true;
            bunker.ActivatedUtc = DateTimeOffset.UtcNow;
            bunker.LastActiveUtc = bunker.ActivatedUtc;
            bunker.ActiveCount++;
        }

        await _bunkers.UpdateEntityAsync(ToEntity(bunker), ETag.All, TableUpdateMode.Replace);
        return bunker;
    }

    /// <summary>End the night: archive whatever was active, then reset every bunker's nightly state.</summary>
    public async Task<NightRecord> ClearNightAsync()
    {
        await EnsureTablesAsync();
        var all = await GetBunkersAsync();
        var active = all.Where(b => b.IsActive).ToList();

        var record = new NightRecord
        {
            Id = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"),
            ClearedUtc = DateTimeOffset.UtcNow,
            Entries = active.Select(b => new NightEntry
            {
                BunkerId = b.Id,
                Coords = b.Coords,
                Description = b.Description,
                Code = b.Code
            }).ToList()
        };

        // Only worth archiving if something actually happened.
        if (record.Entries.Count > 0)
        {
            await _nights.AddEntityAsync(new TableEntity(NightPartition, record.Id)
            {
                ["ClearedUtc"] = record.ClearedUtc,
                ["Entries"] = System.Text.Json.JsonSerializer.Serialize(record.Entries)
            });
        }

        foreach (var bunker in active)
        {
            bunker.IsActive = false;
            bunker.Code = null;
            bunker.ActivatedUtc = null;
            await _bunkers.UpdateEntityAsync(ToEntity(bunker), ETag.All, TableUpdateMode.Replace);
        }

        return record;
    }

    public async Task<List<NightRecord>> GetNightsAsync(int take = 30)
    {
        await EnsureTablesAsync();
        var results = new List<NightRecord>();
        await foreach (var entity in _nights.QueryAsync<TableEntity>(e => e.PartitionKey == NightPartition))
        {
            var json = entity.GetString("Entries");
            results.Add(new NightRecord
            {
                Id = entity.RowKey,
                ClearedUtc = entity.GetDateTimeOffset("ClearedUtc") ?? default,
                Entries = json is null
                    ? new List<NightEntry>()
                    : System.Text.Json.JsonSerializer.Deserialize<List<NightEntry>>(json) ?? new List<NightEntry>()
            });
        }

        return results.OrderByDescending(n => n.ClearedUtc).Take(take).ToList();
    }

    private static Bunker ToBunker(TableEntity e) => new()
    {
        Id = e.RowKey,
        X = e.GetInt32("X") ?? 0,
        Y = e.GetInt32("Y") ?? 0,
        Description = e.GetString("Description") ?? "",
        IsActive = e.GetBoolean("IsActive") ?? false,
        Code = e.GetString("Code"),
        CreatedUtc = e.GetDateTimeOffset("CreatedUtc") ?? default,
        ActivatedUtc = e.GetDateTimeOffset("ActivatedUtc"),
        ActiveCount = e.GetInt32("ActiveCount") ?? 0,
        LastActiveUtc = e.GetDateTimeOffset("LastActiveUtc")
    };

    private static TableEntity ToEntity(Bunker b) => new(BunkerPartition, b.Id)
    {
        ["X"] = b.X,
        ["Y"] = b.Y,
        ["Description"] = b.Description,
        ["IsActive"] = b.IsActive,
        ["Code"] = b.Code,
        ["CreatedUtc"] = b.CreatedUtc,
        ["ActivatedUtc"] = b.ActivatedUtc,
        ["ActiveCount"] = b.ActiveCount,
        ["LastActiveUtc"] = b.LastActiveUtc
    };
}
