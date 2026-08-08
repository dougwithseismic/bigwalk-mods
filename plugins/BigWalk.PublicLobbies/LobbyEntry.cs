using System;

namespace BigWalk.PublicLobbies;

/// <summary>
/// A plain-CLR projection of a LobbyInfo, captured once when a search completes.
///
/// Reading IL2CPP fields is not free and the underlying LobbyInfo can be collected
/// out from under us between searches, so the list model works on these snapshots
/// and only holds the native handle for the moment we actually join.
/// </summary>
public sealed class LobbyEntry
{
    public string WorldName { get; init; } = "";
    public string HostName { get; init; } = "";
    public string JoinCode { get; init; } = "";
    public string Region { get; init; } = "";
    public string Platform { get; init; } = "";
    public bool Crossplay { get; init; }
    public int Players { get; init; }
    public int MaxPlayers { get; init; }

    /// <summary>
    /// True when this record came from the world's host. Member records carry
    /// MaxMembers==1 and no useful occupancy, so they lose during deduping.
    /// </summary>
    public bool IsHostRecord { get; init; }

    public bool PubliclyAdvertised { get; init; } = true;

    // Deliberately no LobbyInfo handle. Joining goes by code, and holding a few
    // hundred IL2CPP object references across frames - with nothing rooting them on
    // the native side - is a use-after-free waiting to happen.

    public bool IsFull => MaxPlayers > 0 && Players >= MaxPlayers;
    public bool IsEmpty => Players <= 0;

    /// <summary>"3/8", or just "3" when the lobby never reported a maximum.</summary>
    public string Occupancy => MaxPlayers > 0 ? $"{Players}/{MaxPlayers}" : Players.ToString();

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        return Contains(WorldName, query)
            || Contains(HostName, query)
            || Contains(JoinCode, query)
            || Contains(Region, query);
    }

    private static bool Contains(string haystack, string needle) =>
        !string.IsNullOrEmpty(haystack) &&
        haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}
