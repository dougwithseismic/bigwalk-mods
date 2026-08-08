using System;
using System.Collections.Generic;

namespace BigWalk.PublicLobbies;

public enum LobbySort
{
    /// <summary>Busiest first - the default, because an empty lobby is a lonely walk.</summary>
    MostPlayers,
    FewestPlayers,
    WorldName,
    HostName,
    Region,
}

/// <summary>
/// Holds the last search result and derives the visible list from it.
///
/// Deliberately free of any Unity or IL2CPP dependency: filtering rules are the part
/// most likely to need tweaking, and keeping them here means they can be reasoned
/// about (and changed) without a game launch in the loop.
/// </summary>
public sealed class LobbyListModel
{
    private readonly List<LobbyEntry> _all = new();
    private readonly List<LobbyEntry> _visible = new();
    private bool _dirty = true;

    // --- filter state -------------------------------------------------------

    private string _query = "";
    private bool _hideFull;
    private bool _hideEmpty;
    private bool _crossplayOnly;
    private LobbySort _sort = LobbySort.MostPlayers;

    public string Query
    {
        get => _query;
        set => Set(ref _query, value ?? "");
    }

    public bool HideFull
    {
        get => _hideFull;
        set => Set(ref _hideFull, value);
    }

    public bool HideEmpty
    {
        get => _hideEmpty;
        set => Set(ref _hideEmpty, value);
    }

    public bool CrossplayOnly
    {
        get => _crossplayOnly;
        set => Set(ref _crossplayOnly, value);
    }

    public LobbySort Sort
    {
        get => _sort;
        set => Set(ref _sort, value);
    }

    /// <summary>Total results from the last search, before filtering.</summary>
    public int TotalCount => _all.Count;

    public IReadOnlyList<LobbyEntry> Visible
    {
        get
        {
            if (_dirty) Rebuild();
            return _visible;
        }
    }

    public void SetResults(IEnumerable<LobbyEntry> entries)
    {
        _all.Clear();
        if (entries != null) _all.AddRange(entries);
        _dirty = true;
    }

    public void CycleSort()
    {
        var values = (LobbySort[])Enum.GetValues(typeof(LobbySort));
        Sort = values[(Array.IndexOf(values, _sort) + 1) % values.Length];
    }

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        _dirty = true;
    }

    private void Rebuild()
    {
        _visible.Clear();

        foreach (var e in _all)
        {
            if (_hideFull && e.IsFull) continue;
            if (_hideEmpty && e.IsEmpty) continue;
            if (_crossplayOnly && !e.Crossplay) continue;
            if (!e.Matches(_query)) continue;
            _visible.Add(e);
        }

        _visible.Sort(Comparer);
        _dirty = false;
    }

    private int Comparer(LobbyEntry a, LobbyEntry b)
    {
        // Every sort falls through to world name so the order is stable between
        // refreshes - cards jumping under the cursor makes the list unusable on a
        // controller, where selection is positional.
        int primary = _sort switch
        {
            LobbySort.MostPlayers => b.Players.CompareTo(a.Players),
            LobbySort.FewestPlayers => a.Players.CompareTo(b.Players),
            LobbySort.WorldName => Text(a.WorldName, b.WorldName),
            LobbySort.HostName => Text(a.HostName, b.HostName),
            LobbySort.Region => Text(a.Region, b.Region),
            _ => 0,
        };

        if (primary != 0) return primary;

        int byWorld = Text(a.WorldName, b.WorldName);
        return byWorld != 0 ? byWorld : Text(a.JoinCode, b.JoinCode);
    }

    private static int Text(string a, string b) =>
        string.Compare(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
}
