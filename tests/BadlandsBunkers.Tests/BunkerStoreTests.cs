using BadlandsBunkers.Api;
using BadlandsBunkers.Shared;

namespace BadlandsBunkers.Tests;

public class BunkerStoreTests : IClassFixture<StoreFixture>
{
    private readonly BunkerStore _store;

    public BunkerStoreTests(StoreFixture fixture) => _store = fixture.Store;

    private Task<Bunker> AddAsync(int x, int y, string description = "somewhere") =>
        _store.CreateBunkerAsync(new BunkerInput { X = x, Y = y, Description = description });

    // ---- creating and reading ----

    [StorageFact]
    public async Task A_created_bunker_comes_back_with_its_details()
    {
        var created = await AddAsync(123, 456, "hillside north of the quarry");

        var fetched = await _store.GetBunkerAsync(created.Id);

        Assert.NotNull(fetched);
        Assert.Equal(123, fetched.X);
        Assert.Equal(456, fetched.Y);
        Assert.Equal("hillside north of the quarry", fetched.Description);
        Assert.False(fetched.IsActive);
        Assert.Null(fetched.Code);
        Assert.Equal(0, fetched.ActiveCount);
    }

    [StorageFact]
    public async Task Descriptions_are_trimmed_on_the_way_in()
    {
        var created = await AddAsync(1, 1, "   by the radio tower   ");

        Assert.Equal("by the radio tower", created.Description);
    }

    [StorageFact]
    public async Task Fetching_an_unknown_id_returns_null_rather_than_throwing()
    {
        Assert.Null(await _store.GetBunkerAsync("does-not-exist"));
    }

    [StorageFact]
    public async Task Updating_changes_coordinates_and_description()
    {
        var created = await AddAsync(10, 20, "first guess");

        await _store.UpdateBunkerAsync(created.Id, new BunkerInput { X = 11, Y = 21, Description = "actually here" });

        var fetched = await _store.GetBunkerAsync(created.Id);
        Assert.Equal(11, fetched!.X);
        Assert.Equal(21, fetched.Y);
        Assert.Equal("actually here", fetched.Description);
    }

    [StorageFact]
    public async Task Updating_an_unknown_bunker_returns_null()
    {
        Assert.Null(await _store.UpdateBunkerAsync("nope", new BunkerInput { X = 1, Y = 1 }));
    }

    [StorageFact]
    public async Task A_deleted_bunker_is_gone()
    {
        var created = await AddAsync(5, 5);

        await _store.DeleteBunkerAsync(created.Id);

        Assert.Null(await _store.GetBunkerAsync(created.Id));
    }

    // ---- activation ----

    [StorageFact]
    public async Task Activating_marks_it_active_and_counts_the_night()
    {
        var created = await AddAsync(30, 40);

        var activated = await _store.SetActiveAsync(created.Id, true);

        Assert.True(activated!.IsActive);
        Assert.Equal(1, activated.ActiveCount);
        Assert.NotNull(activated.ActivatedUtc);
        Assert.NotNull(activated.LastActiveUtc);
    }

    [StorageFact]
    public async Task Activating_something_already_active_does_not_double_count()
    {
        // Two people on the radio can easily tap Activate on the same bunker.
        var created = await AddAsync(31, 41);
        await _store.SetActiveAsync(created.Id, true);

        var again = await _store.SetActiveAsync(created.Id, true);

        Assert.Equal(1, again!.ActiveCount);
    }

    [StorageFact]
    public async Task Deactivating_clears_the_code_but_keeps_the_history_count()
    {
        var created = await AddAsync(32, 42);
        await _store.SetActiveAsync(created.Id, true);
        await _store.SetCodeAsync(created.Id, "1234");

        var deactivated = await _store.SetActiveAsync(created.Id, false);

        Assert.False(deactivated!.IsActive);
        Assert.Null(deactivated.Code);
        Assert.Null(deactivated.ActivatedUtc);
        Assert.Equal(1, deactivated.ActiveCount);        // it still happened
        Assert.NotNull(deactivated.LastActiveUtc);
    }

    [StorageFact]
    public async Task Activating_on_a_later_night_increments_the_count()
    {
        var created = await AddAsync(33, 43);

        await _store.SetActiveAsync(created.Id, true);
        await _store.SetActiveAsync(created.Id, false);
        var third = await _store.SetActiveAsync(created.Id, true);

        Assert.Equal(2, third!.ActiveCount);
    }

    [StorageFact]
    public async Task Activating_an_unknown_bunker_returns_null()
    {
        Assert.Null(await _store.SetActiveAsync("nope", true));
    }

    // ---- codes ----

    [StorageFact]
    public async Task Setting_a_code_on_an_inactive_bunker_activates_it()
    {
        // Typing a code you just decoded is the clearest possible signal it's tonight's.
        var created = await AddAsync(50, 60);

        var withCode = await _store.SetCodeAsync(created.Id, "4821");

        Assert.Equal("4821", withCode!.Code);
        Assert.True(withCode.IsActive);
        Assert.Equal(1, withCode.ActiveCount);
    }

    [StorageFact]
    public async Task Setting_a_code_on_an_already_active_bunker_does_not_recount_it()
    {
        var created = await AddAsync(51, 61);
        await _store.SetActiveAsync(created.Id, true);

        var withCode = await _store.SetCodeAsync(created.Id, "4821");

        Assert.Equal(1, withCode!.ActiveCount);
    }

    [StorageFact]
    public async Task Correcting_a_mistyped_code_replaces_it()
    {
        var created = await AddAsync(52, 62);
        await _store.SetCodeAsync(created.Id, "1111");

        var corrected = await _store.SetCodeAsync(created.Id, "2222");

        Assert.Equal("2222", corrected!.Code);
        Assert.Equal(1, corrected.ActiveCount);
    }

    [StorageFact]
    public async Task Codes_are_trimmed()
    {
        var created = await AddAsync(53, 63);

        var withCode = await _store.SetCodeAsync(created.Id, "  7788  ");

        Assert.Equal("7788", withCode!.Code);
    }

    [StorageFact]
    public async Task Blanking_a_code_clears_it_without_deactivating()
    {
        var created = await AddAsync(54, 64);
        await _store.SetCodeAsync(created.Id, "9999");

        var cleared = await _store.SetCodeAsync(created.Id, "   ");

        Assert.Null(cleared!.Code);
        Assert.True(cleared.IsActive);   // still one of tonight's, we just don't know the code
    }

    [StorageFact]
    public async Task Blanking_a_code_on_an_inactive_bunker_does_not_activate_it()
    {
        var created = await AddAsync(55, 65);

        var result = await _store.SetCodeAsync(created.Id, "");

        Assert.Null(result!.Code);
        Assert.False(result.IsActive);
        Assert.Equal(0, result.ActiveCount);
    }

    // ---- listing ----

    [StorageFact]
    public async Task The_list_puts_active_bunkers_first_then_sorts_by_coordinate()
    {
        var fixtureStore = new BunkerStore(Azurite.ConnectionString, "t" + Guid.NewGuid().ToString("n")[..12]);
        try
        {
            var west = await fixtureStore.CreateBunkerAsync(new BunkerInput { X = 100, Y = 100 });
            var middle = await fixtureStore.CreateBunkerAsync(new BunkerInput { X = 200, Y = 100 });
            var east = await fixtureStore.CreateBunkerAsync(new BunkerInput { X = 300, Y = 100 });
            await fixtureStore.SetActiveAsync(east.Id, true);

            var list = await fixtureStore.GetBunkersAsync();

            Assert.Equal(east.Id, list[0].Id);      // active floats to the top
            Assert.Equal(west.Id, list[1].Id);      // then west-to-east
            Assert.Equal(middle.Id, list[2].Id);
        }
        finally
        {
            await fixtureStore.DeleteTablesAsync();
        }
    }

    // ---- clearing the night ----

    [StorageFact]
    public async Task Clearing_the_night_archives_the_active_bunkers_and_resets_the_board()
    {
        var store = new BunkerStore(Azurite.ConnectionString, "t" + Guid.NewGuid().ToString("n")[..12]);
        try
        {
            var one = await store.CreateBunkerAsync(new BunkerInput { X = 10, Y = 10, Description = "quarry" });
            var two = await store.CreateBunkerAsync(new BunkerInput { X = 20, Y = 20, Description = "tower" });
            var untouched = await store.CreateBunkerAsync(new BunkerInput { X = 30, Y = 30, Description = "dam" });
            await store.SetCodeAsync(one.Id, "1234");
            await store.SetActiveAsync(two.Id, true);   // active but never decoded

            var record = await store.ClearNightAsync();

            // The night is archived, including the one we never got a code for.
            Assert.Equal(2, record.Entries.Count);
            var archivedOne = record.Entries.Single(e => e.BunkerId == one.Id);
            Assert.Equal("1234", archivedOne.Code);
            Assert.Equal("010,010", archivedOne.Coords);
            Assert.Equal("quarry", archivedOne.Description);
            Assert.Null(record.Entries.Single(e => e.BunkerId == two.Id).Code);

            // The board is reset, but the tally of how often each came up survives.
            var all = await store.GetBunkersAsync();
            Assert.All(all, b => Assert.False(b.IsActive));
            Assert.All(all, b => Assert.Null(b.Code));
            Assert.Equal(1, all.Single(b => b.Id == one.Id).ActiveCount);
            Assert.NotNull(all.Single(b => b.Id == one.Id).LastActiveUtc);
            Assert.Equal(0, all.Single(b => b.Id == untouched.Id).ActiveCount);
        }
        finally
        {
            await store.DeleteTablesAsync();
        }
    }

    [StorageFact]
    public async Task Clearing_an_empty_board_archives_nothing()
    {
        var store = new BunkerStore(Azurite.ConnectionString, "t" + Guid.NewGuid().ToString("n")[..12]);
        try
        {
            await store.CreateBunkerAsync(new BunkerInput { X = 1, Y = 1 });

            var record = await store.ClearNightAsync();

            Assert.Empty(record.Entries);
            Assert.Empty(await store.GetNightsAsync());   // no junk entry in History
        }
        finally
        {
            await store.DeleteTablesAsync();
        }
    }

    [StorageFact]
    public async Task Archived_nights_come_back_newest_first()
    {
        var store = new BunkerStore(Azurite.ConnectionString, "t" + Guid.NewGuid().ToString("n")[..12]);
        try
        {
            var bunker = await store.CreateBunkerAsync(new BunkerInput { X = 7, Y = 7, Description = "ridge" });

            await store.SetCodeAsync(bunker.Id, "1111");
            await store.ClearNightAsync();

            await Task.Delay(1100);   // night ids are second-resolution

            await store.SetCodeAsync(bunker.Id, "2222");
            await store.ClearNightAsync();

            var nights = await store.GetNightsAsync();

            Assert.Equal(2, nights.Count);
            Assert.Equal("2222", nights[0].Entries.Single().Code);
            Assert.Equal("1111", nights[1].Entries.Single().Code);
            Assert.True(nights[0].ClearedUtc > nights[1].ClearedUtc);

            // And the bunker has been seen on both nights.
            Assert.Equal(2, (await store.GetBunkerAsync(bunker.Id))!.ActiveCount);
        }
        finally
        {
            await store.DeleteTablesAsync();
        }
    }
}
