using System;
using System.Collections.Generic;

namespace BigWalk.PublicLobbies;

/// <summary>
/// Owns "when do we search", so the button and the auto-refresh timer cannot fight
/// each other or hammer EOS.
///
/// EOS rate-limits lobby search per client, and a refresh button is the one control
/// users will mash when a list looks empty - exactly the moment a throttle response
/// makes it look even emptier. So a manual refresh is gated by a short cooldown, and
/// the button reports its own remaining time rather than silently doing nothing.
/// </summary>
public sealed class RefreshController
{
    private readonly LobbySearchService _search;
    private readonly LobbyListModel _model;

    private float _lastSearchAt = float.NegativeInfinity;
    private float _now;

    public RefreshController(LobbySearchService search, LobbyListModel model)
    {
        _search = search;
        _model = model;
    }

    /// <summary>Seconds a manual refresh must wait after the previous search.</summary>
    public float Cooldown { get; set; } = 5f;

    /// <summary>
    /// Seconds between automatic refreshes while the list is on screen. 0 disables.
    ///
    /// Off by default. A refresh reorders the list under the cursor, which is
    /// disorienting mid-browse, and the result set EOS returns varies between calls
    /// anyway - so rows visibly shuffle for no gain. The Refresh button is explicit.
    /// </summary>
    public float AutoInterval { get; set; }

    public bool IsSearching => _search.IsSearching;

    /// <summary>Last error, or null. Shown in place of the list so failures aren't silent.</summary>
    public string LastError { get; private set; }

    /// <summary>True once at least one search has completed, successfully or not.</summary>
    public bool HasSearched { get; private set; }

    public float CooldownRemaining =>
        Math.Max(0f, (_lastSearchAt + Cooldown) - _now);

    public bool CanRefresh => !IsSearching && CooldownRemaining <= 0f;

    /// <summary>Fired whenever results land, so the view can rebuild its cards.</summary>
    public event Action Updated;

    /// <summary>
    /// Drive from Update. <paramref name="time"/> is passed in rather than read from
    /// UnityEngine.Time so the cooldown logic stays testable off the game loop.
    /// </summary>
    public void Tick(float time, bool listVisible)
    {
        _now = time;
        _search.Pump();

        if (!listVisible || IsSearching) return;

        if (AutoInterval > 0f && _now - _lastSearchAt >= AutoInterval)
            Begin();
    }

    /// <summary>Returns false if the request was swallowed by the cooldown.</summary>
    public bool RequestManual()
    {
        if (!CanRefresh) return false;
        Begin();
        return true;
    }

    /// <summary>Ignores the cooldown. For opening the menu, where a stale list is worse.</summary>
    public void ForceRefresh()
    {
        if (IsSearching) return;
        Begin();
    }

    /// <summary>
    /// Set when a search ran only to populate the game's cached search filter. That
    /// first search uses the stock (small) result cap, so the wide one is queued to
    /// follow it immediately rather than waiting for the auto-refresh interval.
    /// </summary>
    private bool _primed;

    private void Begin()
    {
        // Stamp the time up front: a search that fails to start still counts, or a
        // persistent failure becomes a tight retry loop.
        _lastSearchAt = _now;

        bool wasPrimed = LobbySearchService.FilterIsCached;

        _search.Begin(
            Plugin.Instance?.MaxResults?.Value ?? LobbySearchService.MaxSearchResults,
            OnResults,
            OnError);

        // If that call had to prime the cached filter, its results came from the
        // stock search and are capped well below what we asked for.
        _primed = !wasPrimed;
    }

    private void OnResults(List<LobbyEntry> entries)
    {
        HasSearched = true;
        LastError = null;
        _model.SetResults(entries);
        Updated?.Invoke();

        if (_primed && LobbySearchService.FilterIsCached)
        {
            _primed = false;
            Plugin.Trace.LogInfo("Filter cached; re-running the search at the full result cap.");
            Begin();
        }
    }

    private void OnError(string message)
    {
        HasSearched = true;
        LastError = message;
        Updated?.Invoke();
    }
}
