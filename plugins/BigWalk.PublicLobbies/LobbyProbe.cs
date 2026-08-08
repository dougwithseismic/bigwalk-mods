using System;
using System.Text;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

using Il2CppTask = Il2CppSystem.Threading.Tasks.Task<Il2CppSystem.Collections.Generic.List<LobbyInfo>>;

namespace BigWalk.PublicLobbies;

/// <summary>
/// Reconnaissance pass, not shipping behaviour. Answers the two questions the real
/// UI depends on and that cannot be settled by reading the binary:
///
///   1. Does EOSLobbyManager.FindPublicLobbies() actually return retail lobbies?
///      Vanilla hosts only advertise as public if the game creates lobbies with a
///      public permission level - if it doesn't, a browser has nothing to browse.
///   2. What is in each LobbyInfo, and in particular is BucketId something useful
///      (a region) or a fixed constant? That decides whether cards can show a
///      latency proxy, since the bundled EOS SDK exposes no ping API at all.
///
/// It also dumps the JoinMenu widget tree, because the public cards are going to be
/// clones of the game's own friend-card prefab and we need to know its shape.
/// </summary>
public class LobbyProbe : MonoBehaviour
{
    // Il2CppInterop requires this constructor to build the managed wrapper around
    // the native object Unity hands back from AddComponent.
    public LobbyProbe(IntPtr ptr) : base(ptr) { }

    private Il2CppTask _pending;
    private readonly LobbySearchService _search = new();

    private void Update()
    {
        var cfg = Plugin.Instance;
        if (cfg == null) return;

        if (Input.GetKeyDown(cfg.ProbeKey.Value)) StartProbe();
        if (Input.GetKeyDown(cfg.DumpUiKey.Value)) DumpJoinMenu();
        if (Input.GetKeyDown(cfg.WideProbeKey.Value)) StartWideProbe();

        PollProbe();
        _search.Pump();

        // The browser is a plain object, not a MonoBehaviour, so it borrows this
        // component's update tick rather than owning a GameObject of its own.
        Plugin.Browser?.Tick();
    }

    /// <summary>
    /// The stock search truncates. This asks for the EOS maximum through the same
    /// filter and reports what deduping does to the count, which is what tells us
    /// how the real list should be sized and sorted.
    /// </summary>
    private void StartWideProbe()
    {
        var max = Plugin.Instance.MaxResults.Value;
        Plugin.Trace.LogInfo($"Wide search, maxResults={max}...");

        _search.Begin(max,
            entries =>
            {
                int hosts = 0, members = 0, full = 0;
                foreach (var e in entries)
                {
                    if (e.IsHostRecord) hosts++; else members++;
                    if (e.IsFull) full++;
                }

                Plugin.Trace.LogInfo(
                    $"=== {entries.Count} worlds after dedupe " +
                    $"(host records={hosts}, member-only={members}, full={full}) ===");

                foreach (var e in entries)
                    Plugin.Trace.LogInfo(
                        $"  {e.Occupancy.PadLeft(6)}  {e.WorldName}  (host {e.HostName}, " +
                        $"{e.Platform}, code {e.JoinCode}, region '{e.Region}')");

                Plugin.Trace.LogInfo("=== end ===");
            },
            err => Plugin.Trace.LogError(err));
    }

    // ---------------------------------------------------------------- lobbies

    private void StartProbe()
    {
        if (_pending != null)
        {
            Plugin.Trace.LogInfo("Probe already in flight; ignoring.");
            return;
        }

        var mgr = EOSLobbyManager.Instance;
        if (mgr == null)
        {
            Plugin.Trace.LogWarning(
                "EOSLobbyManager.Instance is null. It is created during EOS init - " +
                "get to the main menu (past the login) and try again.");
            return;
        }

        try
        {
            Plugin.Trace.LogInfo("Calling EOSLobbyManager.FindPublicLobbies()...");
            _pending = mgr.FindPublicLobbies();
            if (_pending == null)
                Plugin.Trace.LogWarning("FindPublicLobbies() returned a null task.");
        }
        catch (Exception e)
        {
            Plugin.Trace.LogError($"FindPublicLobbies() threw: {e}");
            _pending = null;
        }
    }

    private void PollProbe()
    {
        if (_pending == null || !_pending.IsCompleted) return;

        var task = _pending;
        _pending = null;

        try
        {
            if (task.IsFaulted)
            {
                Plugin.Trace.LogError($"Search faulted: {task.Exception}");
                return;
            }

            var results = task.Result;
            if (results == null)
            {
                Plugin.Trace.LogWarning("Search completed with a null result list.");
                return;
            }

            Plugin.Trace.LogInfo($"=== {results.Count} public lobbies ===");
            for (int i = 0; i < results.Count; i++)
                Report(i, results[i]);
            Plugin.Trace.LogInfo("=== end ===");
        }
        catch (Exception e)
        {
            Plugin.Trace.LogError($"Reading search results threw: {e}");
        }
    }

    private static void Report(int index, LobbyInfo info)
    {
        if (info == null)
        {
            Plugin.Trace.LogInfo($"[{index}] <null>");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[{index}] world='{Safe(() => info.worldName)}' " +
                      $"host='{Safe(() => info.userName)}' code='{Safe(() => info.joinCode)}'");
        sb.AppendLine($"      owner='{Safe(() => info.WorldOwnerUserName)}' " +
                      $"ownerPlatformId='{Safe(() => info.WorldOwnerPlatformID)}' " +
                      $"platformID='{Safe(() => info.platformID)}'");
        sb.AppendLine($"      isHost={Safe(() => info.isHost.ToString())} " +
                      $"crossplay={Safe(() => info.crossplay.ToString())} " +
                      $"platform={Safe(() => info.platform.ToString())}");

        // detailsInfo is where the interesting browser metadata lives: the member
        // counts that give us "3/8", the permission level that tells us whether the
        // lobby is genuinely publicly advertised, and BucketId (region candidate).
        try
        {
            var d = info.detailsInfo;
            sb.AppendLine($"      slots: max={d.MaxMembers} available={d.AvailableSlots} " +
                          $"(=> {d.MaxMembers - d.AvailableSlots} in lobby)");
            sb.AppendLine($"      permission={d.PermissionLevel} bucket='{d.BucketId}' " +
                          $"lobbyId='{d.LobbyId}'");
            sb.AppendLine($"      allowInvites={d.AllowInvites} allowJoinById={d.AllowJoinById} " +
                          $"presenceEnabled={d.PresenceEnabled} rtc={d.RTCRoomEnabled} " +
                          $"hostMigration={d.AllowHostMigration}");

            var platforms = d.AllowedPlatformIds;
            if (platforms != null && platforms.Length > 0)
                sb.AppendLine($"      allowedPlatformIds=[{string.Join(", ", ToInts(platforms))}]");
        }
        catch (Exception e)
        {
            sb.AppendLine($"      detailsInfo unreadable: {e.Message}");
        }

        sb.Append(DumpAttributes(info));
        Plugin.Trace.LogInfo(sb.ToString());
    }

    /// <summary>
    /// Enumerates every attribute the host published, rather than only the keys the
    /// game's own constants name.
    ///
    /// This exists to answer one question: can a browser tell that a world is
    /// password-protected? The password is enforced at the Mirror layer after you
    /// connect (HouseAuthenticator, PasswordResponseMessage), and none of the game's
    /// named lobby attributes carry it - so if the answer is anywhere, it is in an
    /// attribute we have never asked for by name.
    /// </summary>
    private static string DumpAttributes(LobbyInfo info)
    {
        var details = Safe2(() => info.lobbyDetails);
        if (details == null) return "      lobbyDetails=null";

        var sb = new StringBuilder();

        try
        {
            var countOptions = new LobbyDetailsGetAttributeCountOptions();
            uint count = details.GetAttributeCount(ref countOptions);
            sb.Append($"      attributes ({count}):");

            for (uint i = 0; i < count; i++)
            {
                var options = new LobbyDetailsCopyAttributeByIndexOptions { AttrIndex = i };
                var result = details.CopyAttributeByIndex(ref options, out var attribute);

                if (result != Result.Success || !attribute.HasValue)
                {
                    sb.Append($" [{i}:{result}]");
                    continue;
                }

                var data = attribute.Value.Data;
                if (!data.HasValue) { sb.Append($" [{i}:no-data]"); continue; }

                sb.Append($" {data.Value.Key}={Describe(data.Value.Value)}");
            }
        }
        catch (Exception e)
        {
            sb.Append($"      attributes unreadable: {e.Message}");
        }

        return sb.ToString();
    }

    private static string Describe(AttributeDataValue value)
    {
        try
        {
            return value.ValueType switch
            {
                AttributeType.String => $"'{value.AsUtf8}'",
                AttributeType.Boolean => value.AsBool.HasValue ? value.AsBool.Value.ToString() : "?",
                AttributeType.Int64 => value.AsInt64.HasValue ? value.AsInt64.Value.ToString() : "?",
                AttributeType.Double => value.AsDouble.HasValue ? value.AsDouble.Value.ToString() : "?",
                _ => "?",
            };
        }
        catch (Exception e)
        {
            return $"<threw: {e.GetType().Name}>";
        }
    }

    private static T Safe2<T>(Func<T> read) where T : class
    {
        try { return read(); }
        catch { return null; }
    }

    private static string[] ToInts(Il2CppStructArray<uint> array)
    {
        var outp = new string[array.Length];
        for (int i = 0; i < array.Length; i++) outp[i] = array[i].ToString();
        return outp;
    }

    // Field reads cross into IL2CPP and can throw on unexpectedly null natives;
    // a probe that dies halfway through tells us less than one that reports "?".
    private static string Safe(Func<string> read)
    {
        try { return read() ?? "<null>"; }
        catch (Exception e) { return $"<threw: {e.GetType().Name}>"; }
    }

    // ---------------------------------------------------------------------- UI

    private void DumpJoinMenu()
    {
        JoinMenu menu = null;
        try
        {
            var all = Resources.FindObjectsOfTypeAll<JoinMenu>();
            if (all != null && all.Length > 0) menu = all[0];
        }
        catch (Exception e)
        {
            Plugin.Trace.LogError($"Looking up JoinMenu threw: {e}");
            return;
        }

        if (menu == null)
        {
            Plugin.Trace.LogWarning("No JoinMenu found. Open the multiplayer menu first.");
            return;
        }

        Plugin.Trace.LogInfo("=== JoinMenu ===");
        Plugin.Trace.LogInfo($"  path: {Path(menu.transform)}");
        Plugin.Trace.LogInfo($"  pollInterval={menu.pollInterval} logVerbose={menu.logVerbose}");

        DumpRef("cardParent", menu.cardParent);
        DumpRef("friendsSection", menu.friendsSection);
        DumpRef("noneFoundCard", menu.noneFoundCard);
        DumpRef("scroller", menu.scroller == null ? null : menu.scroller.transform);
        DumpRef("addressField", menu.addressField == null ? null : menu.addressField.transform);

        var prefab = menu.joinFriendCardPrefab;
        if (prefab == null)
        {
            Plugin.Trace.LogWarning("  joinFriendCardPrefab is null.");
        }
        else
        {
            Plugin.Trace.LogInfo("  --- joinFriendCardPrefab tree ---");
            DumpTree(prefab.transform, 2);
        }

        // The live section matters as much as the prefab: cards the game has already
        // instantiated show the layout components that actually drive spacing.
        if (menu.friendsSection != null)
        {
            Plugin.Trace.LogInfo("  --- friendsSection tree ---");
            DumpTree(menu.friendsSection, 2);
        }

        Plugin.Trace.LogInfo("=== end ===");
    }

    private static void DumpRef(string label, Transform t)
    {
        Plugin.Trace.LogInfo(t == null ? $"  {label}: <null>" : $"  {label}: {Path(t)}");
    }

    private static void DumpTree(Transform t, int depth)
    {
        if (t == null || depth > 8) return;

        var indent = new string(' ', depth * 2);
        var components = new StringBuilder();
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null) continue;
            if (components.Length > 0) components.Append(", ");
            components.Append(c.GetIl2CppType().Name);
        }

        Plugin.Trace.LogInfo($"{indent}{t.name} [{components}]");
        for (int i = 0; i < t.childCount; i++)
            DumpTree(t.GetChild(i), depth + 1);
    }

    private static string Path(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }
}
