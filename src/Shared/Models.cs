using System.ComponentModel.DataAnnotations;

namespace BadlandsBunkers.Shared;

/// <summary>A bunker we've learned about, plus its state for the current night.</summary>
public class Bunker
{
    public string Id { get; set; } = "";

    /// <summary>Map grid easting, as decoded from the morse transmission.</summary>
    public int X { get; set; }

    /// <summary>Map grid northing.</summary>
    public int Y { get; set; }

    public string Description { get; set; } = "";

    /// <summary>True if this bunker is one of tonight's three.</summary>
    public bool IsActive { get; set; }

    /// <summary>Tonight's four-digit lock code, once we've decoded it.</summary>
    public string? Code { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ActivatedUtc { get; set; }

    /// <summary>How many nights this bunker has come up active.</summary>
    public int ActiveCount { get; set; }

    public DateTimeOffset? LastActiveUtc { get; set; }

    public string Coords => $"{X:000},{Y:000}";
}

public class BunkerInput
{
    [Range(0, 9999, ErrorMessage = "X must be 0-9999")]
    public int X { get; set; }

    [Range(0, 9999, ErrorMessage = "Y must be 0-9999")]
    public int Y { get; set; }

    [MaxLength(500)]
    public string Description { get; set; } = "";
}

public class CodeInput
{
    /// <summary>Four-digit lock code. Null or empty clears it.</summary>
    public string? Code { get; set; }
}

public class LoginInput
{
    public string Password { get; set; } = "";
}

public class SessionInfo
{
    public bool Authenticated { get; set; }
}

/// <summary>A snapshot of one night's three active bunkers, written when the night is cleared.</summary>
public class NightRecord
{
    public string Id { get; set; } = "";
    public DateTimeOffset ClearedUtc { get; set; }
    public List<NightEntry> Entries { get; set; } = new();
}

public class NightEntry
{
    public string BunkerId { get; set; } = "";
    public string Coords { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Code { get; set; }
}
