using System;
using System.Collections.Generic;
using Epic.OnlineServices.Lobby;

using Il2CppLobbyList = Il2CppSystem.Collections.Generic.List<LobbyInfo>;
using Il2CppSearchTask = Il2CppSystem.Threading.Tasks.Task<Il2CppSystem.Collections.Generic.List<LobbyInfo>>;

namespace BigWalk.PublicLobbies;

/// <summary>
/// Wraps the game's own EOS lobby search so we can ask for more than the stock
/// browser does, and turns the raw results into deduped <see cref="LobbyEntry"/>.
///
/// Two things make the raw result list unsuitable for direct display:
///
///   * Big Walk advertises a lobby per *player*, not per world. A member's record
///     has MaxMembers==1 and IsHost==false, and carries the world owner's name in
///     the WorldOwner* fields. Showing those verbatim gives a list of phantom
///     one-slot lobbies that duplicate the real worlds behind them.
///   * FindPublicLobbies() passes a small fixed result cap, so a busy evening gets
///     truncated well before EOS runs out of lobbies to return.
/// </summary>
public sealed class LobbySearchService
{
    /// <summary>EOS_LOBBY_MAX_SEARCH_RESULTS - the SDK rejects anything larger.</summary>
    public const uint MaxSearchResults = 200;

    private Il2CppSearchTask _pending;
    private Action<List<LobbyEntry>> _onDone;
    private Action<string> _onError;

    public bool IsSearching => _pending != null;

    /// <summary>
    /// The filter predicate FindPublicLobbies() feeds to its LobbySearch. It is a
    /// compiler-cached lambda, so it only exists once the game has run that path at
    /// least this session; we prime it by calling FindPublicLobbies() once.
    /// Reusing it means our search matches vanilla's semantics exactly - we are
    /// only overriding how many results come back.
    /// </summary>
    private static Il2CppSystem.Action<LobbySearch> PublicFilter =>
        EOSLobbyManager.__c.__9__51_0;

    /// <summary>False until the game has run FindPublicLobbies() at least once.</summary>
    public static bool FilterIsCached
    {
        get
        {
            try { return PublicFilter != null; }
            catch { return false; }
        }
    }

    public bool Begin(uint maxResults, Action<List<LobbyEntry>> onDone, Action<string> onError)
    {
        if (_pending != null) return false;

        _onDone = onDone;
        _onError = onError;

        var mgr = EOSLobbyManager.Instance;
        if (mgr == null)
        {
            Fail("EOSLobbyManager.Instance is null - EOS has not finished initialising.");
            return false;
        }

        try
        {
            var filter = PublicFilter;
            if (filter == null)
            {
                // Cold start: prime the cached lambda. This search is the stock one
                // (small cap), but its results are still perfectly good, and every
                // subsequent search can use the wider cap.
                Plugin.Trace.LogInfo("Public filter not cached yet; priming via FindPublicLobbies().");
                _pending = mgr.FindPublicLobbies();
            }
            else
            {
                _pending = mgr.FindLobbies(Math.Min(maxResults, MaxSearchResults), filter);
            }

            if (_pending == null)
            {
                Fail("Lobby search returned a null task.");
                return false;
            }
        }
        catch (Exception e)
        {
            Fail($"Lobby search threw: {e.Message}");
            return false;
        }

        return true;
    }

    /// <summary>Call every frame; completes the search when EOS is done.</summary>
    public void Pump()
    {
        if (_pending == null || !_pending.IsCompleted) return;

        var task = _pending;
        _pending = null;

        try
        {
            if (task.IsFaulted)
            {
                Fail($"Search faulted: {task.Exception?.Message}");
                return;
            }

            _onDone?.Invoke(Project(task.Result));
        }
        catch (Exception e)
        {
            Fail($"Reading search results threw: {e.Message}");
        }
    }

    private void Fail(string message)
    {
        Plugin.Trace.LogWarning(message);
        _onError?.Invoke(message);
    }

    // --------------------------------------------------------------- projection

    /// <summary>
    /// Collapses the raw per-player records into one entry per world.
    ///
    /// Records are keyed by join code, which is the world's identity. A host record
    /// always wins over a member record, because only the host's carries the real
    /// member counts; member records exist purely so friends can find each other.
    /// </summary>
    public static List<LobbyEntry> Project(Il2CppLobbyList raw)
    {
        var byCode = new Dictionary<string, LobbyEntry>(StringComparer.OrdinalIgnoreCase);
        var anonymous = new List<LobbyEntry>();

        if (raw == null) return anonymous;

        for (int i = 0; i < raw.Count; i++)
        {
            var entry = Convert(raw[i]);
            if (entry == null) continue;

            if (string.IsNullOrEmpty(entry.JoinCode))
            {
                // No code means nothing to join and nothing to dedupe on; keep it
                // out of the map rather than letting "" collapse unrelated worlds.
                anonymous.Add(entry);
                continue;
            }

            if (!byCode.TryGetValue(entry.JoinCode, out var existing) || Prefer(entry, existing))
                byCode[entry.JoinCode] = entry;
        }

        var result = new List<LobbyEntry>(byCode.Count + anonymous.Count);
        result.AddRange(byCode.Values);
        result.AddRange(anonymous);
        return result;
    }

    /// <summary>True if <paramref name="candidate"/> is the better record for a world.</summary>
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
        catch (Exception e)
        {
            Plugin.Trace.LogWarning($"Skipping unreadable lobby: {e.Message}");
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
