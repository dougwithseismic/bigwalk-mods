using System;
using System.Collections.Generic;
using Epic.OnlineServices.Lobby;
using UnityEngine;

using Il2CppLobbyList = Il2CppSystem.Collections.Generic.List<LobbyInfo>;
using Il2CppSearchTask = Il2CppSystem.Threading.Tasks.Task<Il2CppSystem.Collections.Generic.List<LobbyInfo>>;

namespace BigWalk.PublicLobbies;

/// <summary>
/// Runs the game's own EOS lobby search and turns the results into deduplicated
/// <see cref="LobbyEntry"/>.
///
/// Three things make a single call unsuitable:
///
///   * EOS returns a varying *subset* of matching lobbies per search, not a stable
///     page. One query therefore never sees the whole population, and repeating it
///     returns a different sample - which is why the room count drifts between
///     refreshes even when nothing has opened or closed. The fix is to run several
///     searches and union the results.
///   * Big Walk advertises a lobby per *player*, not per world. A member's record has
///     MaxMembers==1 and IsHost==false and carries the world owner in its WorldOwner*
///     fields, so showing results verbatim gives a list of phantom one-slot rooms.
///   * FindPublicLobbies() passes a small fixed result cap.
/// </summary>
public sealed class LobbySearchService
{
    /// <summary>EOS_LOBBY_MAX_SEARCH_RESULTS - the SDK rejects anything larger.</summary>
    public const uint MaxSearchResults = 200;

    /// <summary>
    /// Concurrent searches in flight. EOS rate-limits per client, and a throttled
    /// search returns nothing - which looks exactly like an empty lobby list.
    /// </summary>
    private const int MaxConcurrent = 8;

    /// <summary>Give up on stragglers rather than leaving the UI searching forever.</summary>
    private const float TimeoutSeconds = 20f;

    private readonly List<Il2CppSearchTask> _inFlight = new();
    private readonly Dictionary<string, LobbyEntry> _union =
        new(StringComparer.OrdinalIgnoreCase);

    private Action<List<LobbyEntry>> _onDone;
    private Action<string> _onError;

    private int _roundsTotal;
    private int _roundsStarted;
    private int _roundsCompleted;
    private uint _maxResults;
    private float _deadline;
    private bool _reportedFirst;

    public bool IsSearching { get; private set; }

    /// <summary>
    /// The filter predicate FindPublicLobbies() feeds to its LobbySearch. It is a
    /// compiler-cached lambda, so it only exists once the game has run that path at
    /// least once this session. Reusing it means our searches match vanilla's
    /// semantics exactly - we only override how many results come back, and how often.
    /// </summary>
    private static Il2CppSystem.Action<LobbySearch> PublicFilter =>
        EOSLobbyManager.__c.__9__51_0;

    public static bool FilterIsCached
    {
        get
        {
            try { return PublicFilter != null; }
            catch { return false; }
        }
    }

    public bool Begin(uint maxResults, int rounds, Action<List<LobbyEntry>> onDone, Action<string> onError)
    {
        if (IsSearching) return false;

        _onDone = onDone;
        _onError = onError;
        _maxResults = Math.Min(maxResults, MaxSearchResults);
        _roundsTotal = Mathf.Clamp(rounds, 1, 30);
        _roundsStarted = 0;
        _roundsCompleted = 0;
        _reportedFirst = false;
        _deadline = Time.unscaledTime + TimeoutSeconds;

        _union.Clear();
        _inFlight.Clear();

        if (EOSLobbyManager.Instance == null)
        {
            Fail("EOSLobbyManager.Instance is null - EOS has not finished initialising.");
            return false;
        }

        IsSearching = true;
        return true;
    }

    /// <summary>Call every frame: starts rounds and harvests completed ones.</summary>
    public void Pump()
    {
        if (!IsSearching) return;

        try
        {
            StartRounds();
            HarvestCompleted();

            bool done = _roundsCompleted >= _roundsTotal && _inFlight.Count == 0;
            bool timedOut = Time.unscaledTime >= _deadline;

            if (done || timedOut)
            {
                if (timedOut && !done)
                    Plugin.Trace.LogWarning(
                        $"Lobby search timed out with {_roundsCompleted}/{_roundsTotal} rounds done; " +
                        $"showing {_union.Count} rooms found so far.");

                Finish();
            }
        }
        catch (Exception e)
        {
            Fail($"Lobby search failed: {e.Message}");
            Finish();
        }
    }

    /// <summary>
    /// One new search per frame, up to the concurrency cap. Staggering them matters:
    /// firing every round at once is the reliable way to get rate-limited.
    /// </summary>
    private void StartRounds()
    {
        if (_roundsStarted >= _roundsTotal) return;
        if (_inFlight.Count >= MaxConcurrent) return;

        var mgr = EOSLobbyManager.Instance;
        if (mgr == null) return;

        Il2CppSearchTask task;

        var filter = PublicFilter;
        if (filter == null)
        {
            // Cold start: the cached lambda only exists after the game has run this
            // path once. Prime it - those results are still perfectly good, just
            // capped lower - and every later round uses the wider search.
            task = mgr.FindPublicLobbies();
        }
        else
        {
            task = mgr.FindLobbies(_maxResults, filter);
        }

        if (task == null)
        {
            _roundsCompleted++;
            return;
        }

        _inFlight.Add(task);
        _roundsStarted++;
    }

    private void HarvestCompleted()
    {
        for (int i = _inFlight.Count - 1; i >= 0; i--)
        {
            var task = _inFlight[i];
            if (task == null) { _inFlight.RemoveAt(i); _roundsCompleted++; continue; }
            if (!task.IsCompleted) continue;

            _inFlight.RemoveAt(i);
            _roundsCompleted++;

            if (task.IsFaulted) continue;

            try { Merge(task.Result); }
            catch (Exception e) { Plugin.Trace.LogWarning($"Reading a search round failed: {e.Message}"); }
        }

        // Show the first round immediately rather than making the player wait for
        // every round to land; later rounds only ever add rooms.
        if (!_reportedFirst && _union.Count > 0)
        {
            _reportedFirst = true;
            _onDone?.Invoke(Snapshot());
        }
    }

    private void Finish()
    {
        IsSearching = false;
        _inFlight.Clear();
        _onDone?.Invoke(Snapshot());
    }

    private List<LobbyEntry> Snapshot() => new(_union.Values);

    private void Fail(string message)
    {
        Plugin.Trace.LogWarning(message);
        IsSearching = false;
        _onError?.Invoke(message);
    }

    // --------------------------------------------------------------- projection

    /// <summary>
    /// Folds one round's results into the union, keyed by join code - the world's
    /// identity. A host record always beats a member record, because only the host's
    /// carries real member counts.
    /// </summary>
    private void Merge(Il2CppLobbyList raw)
    {
        if (raw == null) return;

        for (int i = 0; i < raw.Count; i++)
        {
            var entry = Convert(raw[i]);
            if (entry == null || string.IsNullOrEmpty(entry.JoinCode)) continue;

            if (!_union.TryGetValue(entry.JoinCode, out var existing) || Prefer(entry, existing))
                _union[entry.JoinCode] = entry;
        }
    }

    private static bool Prefer(LobbyEntry candidate, LobbyEntry existing)
    {
        if (candidate.IsHostRecord != existing.IsHostRecord) return candidate.IsHostRecord;

        // Between two records of the same kind, trust the one that knows about more
        // seats - a stale record can report a smaller lobby than actually exists.
        return candidate.MaxPlayers > existing.MaxPlayers;
    }

    private static LobbyEntry Convert(LobbyInfo info)
    {
        if (info == null) return null;

        try
        {
            int max = 0, players = 0;
            string region = "";
            bool advertised = true;

            try
            {
                var d = info.detailsInfo;
                max = (int)d.MaxMembers;
                players = Math.Max(0, max - (int)d.AvailableSlots);
                region = Str(d.BucketId);
                advertised = d.PermissionLevel == LobbyPermissionLevel.Publicadvertised;
            }
            catch
            {
                // A lobby whose details we cannot read is still listable by name.
            }

            bool isHost = info.isHost;

            // For a member record the useful identity is the world owner, not the
            // member who happens to be advertising it.
            string host = isHost
                ? Or(info.userName, info.WorldOwnerUserName)
                : Or(info.WorldOwnerUserName, info.userName);

            return new LobbyEntry
            {
                WorldName = Or(info.worldName, "Unnamed"),
                HostName = host,
                JoinCode = info.joinCode ?? "",
                Region = region,
                Platform = SafePlatform(info),
                Crossplay = info.crossplay,
                Players = players,
                MaxPlayers = max,
                IsHostRecord = isHost,
                PubliclyAdvertised = advertised,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string SafePlatform(LobbyInfo info)
    {
        try { return info.platform.ToString(); }
        catch { return ""; }
    }

    /// <summary>
    /// Utf8String is an EOS wrapper whose interop ToString() yields the type name
    /// rather than the text, so the characters have to be taken from Utf16.
    /// </summary>
    private static string Str(Epic.OnlineServices.Utf8String s)
    {
        if (s == null) return "";
        try { return s.Utf16 ?? ""; }
        catch { return ""; }
    }

    private static string Or(string a, string b) =>
        !string.IsNullOrWhiteSpace(a) ? a : (b ?? "");
}
