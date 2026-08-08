using System;
using UnityEngine;

namespace BigWalk.PublicLobbies;

/// <summary>
/// IMGUI lobby browser.
///
/// The native section renders correctly but cannot be driven: this menu is not
/// raycast-based, it is an explicit navigation graph (ManagedButton.SetNavagitationTargets,
/// isDefaultSelection, NoCurrentActiveSelection), and cloned widgets are not in it, so
/// the game never dispatches activation to them - Button.onClick simply never fires.
/// Rebuilding that graph across a list that changes every refresh is a lot of fragile
/// surface, so the interactive browser lives here instead, where input, scrolling and
/// text sizing are all ours.
/// </summary>
public class LobbyOverlay : MonoBehaviour
{
    // Field initialisers, not Awake. DevOverlay - the one IMGUI window proven stable
    // in this game - has no Awake at all, and building IL2CPP-facing state there runs
    // at plugin-load time against a not-yet-fully-constructed injected object.
    private readonly LobbyListModel _model = new();
    private readonly LobbySearchService _search = new();
    private readonly RefreshController _refresh;

    public LobbyOverlay(IntPtr ptr) : base(ptr)
    {
        _refresh = new RefreshController(_search, _model);
    }

    private bool _visible;
    private Rect _window = new Rect(60f, 60f, 760f, 720f);
    private Vector2 _scroll;
    private string _query = "";

    private CursorLockMode _priorLockState = CursorLockMode.Locked;
    private bool _priorCursorVisible;


    private void Update()
    {
        if (Input.GetKeyDown(Plugin.Instance.BrowserKey.Value))
            SetVisible(!_visible);

        // Ticked even while hidden so an in-flight search still completes, but only
        // auto-refreshed while on screen - no point polling EOS for a closed window.
        _refresh.Tick(Time.unscaledTime, _visible);

        if (_visible)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void SetVisible(bool visible)
    {
        if (visible == _visible) return;
        _visible = visible;

        if (visible)
        {
            _priorLockState = Cursor.lockState;
            _priorCursorVisible = Cursor.visible;
            _refresh.ForceRefresh();
        }
        else
        {
            Cursor.lockState = _priorLockState;
            Cursor.visible = _priorCursorVisible;
        }
    }

    private void OnGUI()
    {
        if (!_visible) return;

        // Delegate built inline, exactly as DevOverlay does it. Caching it in a field
        // was my own idea and is what turned an intermittent crash into an immediate
        // one - the pattern below is the one this game is known to tolerate.
        _window = GUI.Window(
            0x8128, _window, (GUI.WindowFunction)DrawWindow, "Big Walk - Public Lobbies");
    }

    private void DrawWindow(int id)
    {
        // An exception escaping a window function leaves IMGUI's layout stack
        // unbalanced, which then throws every frame and can take the process with it.
        try
        {
            DrawToolbar();
            GUILayout.Space(4f);
            DrawList();
        }
        catch (Exception e)
        {
            if (!_reportedDrawError)
            {
                _reportedDrawError = true;
                Plugin.Trace.LogError($"Overlay draw threw (further occurrences suppressed): {e}");
            }
        }

    }

    private bool _reportedDrawError;

    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Search", GUILayout.Width(52f));

        var typed = GUILayout.TextField(_query, GUILayout.MinWidth(200f));
        if (typed != _query)
        {
            _query = typed;
            _model.Query = typed;
        }

        if (GUILayout.Button("Clear", GUILayout.Width(60f)))
        {
            _query = "";
            _model.Query = "";
        }

        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();

        // No GUI.enabled toggling: the cooldown is expressed in the label, and
        // RequestManual already refuses while one is in flight.
        var cooldown = _refresh.CooldownRemaining;
        var refreshLabel = _refresh.IsSearching ? "Searching..."
                         : cooldown > 0f ? $"Refresh ({cooldown:0}s)"
                         : "Refresh";
        if (GUILayout.Button(refreshLabel, GUILayout.Width(130f)))
            _refresh.RequestManual();

        if (GUILayout.Button($"Sort: {SortLabel(_model.Sort)}", GUILayout.Width(150f)))
            _model.CycleSort();

        if (GUILayout.Button(_model.HideFull ? "Full: hidden" : "Full: shown", GUILayout.Width(120f)))
            _model.HideFull = !_model.HideFull;

        if (GUILayout.Button(_model.HideEmpty ? "Empty: hidden" : "Empty: shown", GUILayout.Width(130f)))
            _model.HideEmpty = !_model.HideEmpty;

        if (GUILayout.Button(_model.CrossplayOnly ? "Crossplay only" : "All platforms", GUILayout.Width(130f)))
            _model.CrossplayOnly = !_model.CrossplayOnly;

        GUILayout.EndHorizontal();

        GUILayout.Label(StatusLine());
    }

    private string StatusLine()
    {
        if (_refresh.LastError != null) return $"Search failed: {_refresh.LastError}";
        if (_refresh.IsSearching && _model.TotalCount == 0) return "Searching for lobbies...";
        if (!_refresh.HasSearched) return "Press Refresh to search.";
        if (_model.TotalCount == 0) return "No public lobbies found.";

        return _model.Visible.Count == _model.TotalCount
            ? $"{_model.TotalCount} lobbies"
            : $"{_model.Visible.Count} of {_model.TotalCount} lobbies";
    }

    private void DrawList()
    {
        _scroll = GUILayout.BeginScrollView(_scroll);

        var rows = _model.Visible;
        if (rows.Count == 0)
        {
            GUILayout.Label("Nothing matches the current filters.");
        }
        else
        {
            for (int i = 0; i < rows.Count; i++)
                DrawRow(rows[i]);
        }

        GUILayout.EndScrollView();
    }

    private void DrawRow(LobbyEntry entry)
    {
        // One line per row, built as a single string: fewer IMGUI calls per frame,
        // and with 200 rows that difference is worth having.
        var detail = $"{entry.Occupancy.PadLeft(5)}  {entry.WorldName}";

        GUILayout.BeginHorizontal();
        GUILayout.Label(detail);
        GUILayout.FlexibleSpace();

        if (GUILayout.Button(entry.IsFull ? "Full" : "Join", GUILayout.Width(80f)))
        {
            if (!entry.IsFull) Join(entry);
        }

        GUILayout.EndHorizontal();

        var sub = $"      host: {(string.IsNullOrEmpty(entry.HostName) ? "?" : entry.HostName)}";
        if (!string.IsNullOrEmpty(entry.Platform)) sub += $"   {entry.Platform}";
        if (!entry.Crossplay) sub += "   (no crossplay)";
        sub += $"   code {entry.JoinCode}";
        GUILayout.Label(sub);
    }

    /// <summary>
    /// Prefers JoinMenu.ConnectTo, which is the path the friend cards and the join-code
    /// box use, so auth checks and error popups behave normally. Falls back to the EOS
    /// manager directly when the menu is not loaded - the overlay can be opened from
    /// anywhere, including in-game.
    /// </summary>
    private void Join(LobbyEntry entry)
    {
        if (string.IsNullOrEmpty(entry.JoinCode))
        {
            Plugin.Trace.LogWarning("That lobby has no join code.");
            return;
        }

        Plugin.Trace.LogInfo($"Joining '{entry.WorldName}' ({entry.JoinCode})...");
        SetVisible(false);

        try
        {
            var menus = Resources.FindObjectsOfTypeAll<JoinMenu>();
            if (menus != null && menus.Length > 0 && menus[0] != null)
            {
                menus[0].ConnectTo(entry.JoinCode);
                return;
            }

            var mgr = EOSLobbyManager.Instance;
            if (mgr != null) mgr.FindLobbyAndConnectByCode(entry.JoinCode);
            else Plugin.Trace.LogError("No JoinMenu and no EOSLobbyManager; cannot join.");
        }
        catch (Exception e)
        {
            Plugin.Trace.LogError($"Join failed: {e}");
        }
    }

    private static string SortLabel(LobbySort sort) => sort switch
    {
        LobbySort.MostPlayers => "busiest",
        LobbySort.FewestPlayers => "quietest",
        LobbySort.WorldName => "world",
        LobbySort.HostName => "host",
        LobbySort.Region => "region",
        _ => sort.ToString(),
    };
}
