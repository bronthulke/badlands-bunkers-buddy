using System.Net;
using BadlandsBunkers.Api;
using BadlandsBunkers.Shared;

namespace BadlandsBunkers.Tests;

public class ReadCookieTests
{
    [Fact]
    public void Finds_the_only_cookie()
    {
        var req = new FakeHttpRequestData("bb_session=abc123");

        Assert.Equal("abc123", req.ReadCookie("bb_session"));
    }

    [Fact]
    public void Finds_a_cookie_among_several()
    {
        var req = new FakeHttpRequestData("theme=dark; bb_session=abc123; other=value");

        Assert.Equal("abc123", req.ReadCookie("bb_session"));
    }

    [Fact]
    public void Finds_a_cookie_sent_in_a_separate_header()
    {
        var req = new FakeHttpRequestData(new[] { "theme=dark", "bb_session=abc123" });

        Assert.Equal("abc123", req.ReadCookie("bb_session"));
    }

    [Fact]
    public void Tolerates_missing_whitespace_between_cookies()
    {
        var req = new FakeHttpRequestData("theme=dark;bb_session=abc123");

        Assert.Equal("abc123", req.ReadCookie("bb_session"));
    }

    [Fact]
    public void Does_not_match_a_cookie_whose_name_merely_ends_with_ours()
    {
        // "evil_bb_session" must not be mistaken for "bb_session".
        var req = new FakeHttpRequestData("evil_bb_session=attacker-value");

        Assert.Null(req.ReadCookie("bb_session"));
    }

    [Fact]
    public void Does_not_match_a_cookie_whose_name_merely_starts_with_ours()
    {
        var req = new FakeHttpRequestData("bb_session_other=attacker-value");

        Assert.Null(req.ReadCookie("bb_session"));
    }

    [Fact]
    public void Returns_null_when_the_cookie_is_absent()
    {
        var req = new FakeHttpRequestData("theme=dark; other=value");

        Assert.Null(req.ReadCookie("bb_session"));
    }

    [Fact]
    public void Returns_null_when_there_is_no_cookie_header_at_all()
    {
        var req = new FakeHttpRequestData();

        Assert.Null(req.ReadCookie("bb_session"));
    }

    [Fact]
    public void Preserves_a_value_containing_a_period()
    {
        // Our tokens are "{expiry}.{signature}" — the dot has to survive parsing.
        var req = new FakeHttpRequestData("bb_session=1700000000.AbC-_dEf");

        Assert.Equal("1700000000.AbC-_dEf", req.ReadCookie("bb_session"));
    }

    [Fact]
    public void Reads_an_empty_value_as_empty_not_missing()
    {
        var req = new FakeHttpRequestData("bb_session=");

        Assert.Equal("", req.ReadCookie("bb_session"));
    }
}

public class ReadJsonTests
{
    [Fact]
    public async Task Deserialises_a_valid_body_using_camelCase()
    {
        var req = new FakeHttpRequestData(body: """{"x":123,"y":456,"description":"by the quarry"}""");

        var input = await req.ReadJsonAsync<BunkerInput>();

        Assert.NotNull(input);
        Assert.Equal(123, input.X);
        Assert.Equal(456, input.Y);
        Assert.Equal("by the quarry", input.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ unclosed")]
    public async Task Returns_null_for_an_unparseable_body_instead_of_throwing(string body)
    {
        var req = new FakeHttpRequestData(body: body);

        Assert.Null(await req.ReadJsonAsync<BunkerInput>());
    }

    [Fact]
    public async Task Returns_null_for_a_literal_json_null()
    {
        var req = new FakeHttpRequestData(body: "null");

        Assert.Null(await req.ReadJsonAsync<BunkerInput>());
    }
}

public class JsonResponseTests
{
    [Fact]
    public async Task Writes_the_payload_and_the_status()
    {
        var req = new FakeHttpRequestData();

        var response = (FakeHttpResponseData)await req.JsonAsync(
            new SessionInfo { Authenticated = true }, HttpStatusCode.Created);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("\"authenticated\":true", response.ReadBodyAsString());
    }

    [Fact]
    public async Task Error_responses_carry_the_message_under_an_error_key()
    {
        var req = new FakeHttpRequestData();

        var response = (FakeHttpResponseData)await req.ErrorAsync(
            HttpStatusCode.Unauthorized, "Not signed in.");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("\"error\":\"Not signed in.\"", response.ReadBodyAsString());
    }
}

public class ValidateBunkerInputTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(123, 456)]
    [InlineData(9999, 9999)]
    public void Accepts_coordinates_in_range(int x, int y)
    {
        Assert.Null(BunkerFunctions.Validate(new BunkerInput { X = x, Y = y, Description = "ok" }));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(10000, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 10000)]
    public void Rejects_coordinates_out_of_range(int x, int y)
    {
        Assert.NotNull(BunkerFunctions.Validate(new BunkerInput { X = x, Y = y, Description = "ok" }));
    }

    [Fact]
    public void Accepts_an_empty_description()
    {
        // Sometimes you get the coordinates before anyone has scouted the spot.
        Assert.Null(BunkerFunctions.Validate(new BunkerInput { X = 1, Y = 1, Description = "" }));
    }

    [Fact]
    public void Rejects_a_description_over_the_limit()
    {
        var input = new BunkerInput { X = 1, Y = 1, Description = new string('a', 501) };

        Assert.NotNull(BunkerFunctions.Validate(input));
    }

    [Fact]
    public void Accepts_a_description_exactly_at_the_limit()
    {
        var input = new BunkerInput { X = 1, Y = 1, Description = new string('a', 500) };

        Assert.Null(BunkerFunctions.Validate(input));
    }
}

public class CoordsFormattingTests
{
    [Theory]
    [InlineData(0, 0, "000,000")]
    [InlineData(75, 34, "075,034")]
    [InlineData(123, 456, "123,456")]
    [InlineData(1234, 5678, "1234,5678")]
    public void Coordinates_are_zero_padded_to_three_digits(int x, int y, string expected)
    {
        Assert.Equal(expected, new Bunker { X = x, Y = y }.Coords);
    }
}
