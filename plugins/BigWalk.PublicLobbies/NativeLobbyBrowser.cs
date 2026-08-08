using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BigWalk.PublicLobbies;

/// <summary>
/// Builds the public lobby section inside the game's own JoinMenu.
///
/// Everything on screen is a clone of a widget the game already ships - the friend
/// card prefab for rows, the Join_Friends subheading for the section header, the
/// card's PlayButton for controls, the join-code box for the search field. Cloning
/// inherits the game's fonts, materials and layout metrics, so the section matches
/// the friends list without restyling anything.
///
/// Rows are parented *directly* to ScrollContents, the same VerticalLayoutGroup that
/// owns Join_Friends and the friend cards. An intermediate container would be laid
/// out as a single row, stacking everything inside it at one position.
/// </summary>
public sealed class NativeLobbyBrowser
{
    /// <summary>Every object we create is named with this prefix so a rebuild can find and remove it.</summary>
    private const string Prefix = "PublicLobbies_";
    private const string CardPrefix = "PublicLobby_";

    private readonly LobbyListModel _model = new();
    private readonly LobbySearchService _search = new();
    private readonly RefreshController _refresh;

    private JoinMenu _menu;
    private Transform _anchor;
    private Transform _cardTemplate;
    private Transform _buttonTemplate;

    private GameObject _header;
    private GameObject _searchRow;
    private GameObject _controls;
    private TMP_InputField _searchField;
    private UiLabel _headerLabel;
    private UiLabel _refreshLabel;
    private UiLabel _sortLabel;
    private UiLabel _fullLabel;
    private UiLabel _crossplayLabel;

    private readonly List<GameObject> _cards = new();
    private bool _built;

    public NativeLobbyBrowser()
    {
        _refresh = new RefreshController(_search, _model);
        _refresh.Updated += Rebuild;
    }

    public LobbyListModel Model => _model;

    // ------------------------------------------------------------------ lifecycle

    public void Attach(JoinMenu menu)
    {
        if (menu == null) return;

        _menu = menu;
        _built = false;

        try
        {
            Build();
            _built = true;
            _refresh.ForceRefresh();
        }
        catch (Exception e)
        {
            Plugin.Trace.LogError($"Failed to build the public lobby section: {e}");
        }
    }

    public void Detach()
    {
        // The list padding belongs to the game's own menu, so give it back rather than
        // leaving the friends list permanently indented by our chrome's height.
        try { RestoreListPadding(); }
        catch (Exception e) { Plugin.Trace.LogWarning($"Restoring list padding failed: {e.Message}"); }

        _menu = null;
    }

    public void Tick()
    {
        if (!_built || _menu == null) return;

        // Tick runs from a MonoBehaviour Update, so an escaping exception is reported
        // by Unity once per frame with no stack of ours attached - which hides both
        // the real fault and everything after it in this method. Catching here keeps
        // the trace and stops one bad frame from disabling scrolling entirely.
        try
        {
            bool visible = _menu.isActiveAndEnabled;
            _refresh.Tick(Time.unscaledTime, visible);

            if (!visible) return;

            UpdateChrome();
            DetectScrollReset();
            HandleScrollWheel();
        }
        catch (Exception e)
        {
            if (!_reportedTickError)
            {
                _reportedTickError = true;
                Plugin.Trace.LogError($"Tick threw (further occurrences suppressed): {e}");
            }
        }
    }

    private bool _reportedTickError;

    private float _expectedScrollY;
    private bool _scrollApplied;
    private bool _reportedReset;

    /// <summary>
    /// If the list refuses to move, the interesting question is whether our offset is
    /// never applied or applied and then overwritten - HouseScroller.Update could be
    /// restoring its own position every frame. This reports which, once.
    /// </summary>
    /// <summary>
    /// The real height of the list.
    ///
    /// ScrollContents has a VerticalLayoutGroup but no ContentSizeFitter, so its rect
    /// stays the size of the viewport however many rows are inside it - children are
    /// simply positioned past the bounds. Reading rect.height therefore always
    /// reports zero overflow and makes scrolling look impossible. The layout group's
    /// preferred height is the number that actually tracks the content.
    /// </summary>
    private static float ContentHeight(RectTransform content)
    {
        // The layout group only recomputes its preferred height during a rebuild, so
        // measuring straight after rows change reads a stale value - that is what
        // makes scrolling die on the first refresh. Force the rebuild, then measure.
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        float preferred = LayoutUtility.GetPreferredHeight(content);

        // Fallback: if the group reports nothing useful, add the rows up directly.
        if (preferred <= content.rect.height)
        {
            float sum = 0f;
            for (int i = 0; i < content.childCount; i++)
            {
                var child = AsRect(content.GetChild(i));
                if (child != null && child.gameObject.activeSelf) sum += child.rect.height;
            }

            var group = content.GetComponent<VerticalLayoutGroup>();
            if (group != null && content.childCount > 1)
            {
                sum += group.spacing * (content.childCount - 1);
                sum += group.padding.top + group.padding.bottom;
            }

            preferred = Mathf.Max(preferred, sum);
        }

        return Mathf.Max(preferred, content.rect.height);
    }

    /// <summary>
    /// Converts a Transform to its RectTransform.
    ///
    /// `transform as RectTransform` does NOT work across Il2CppInterop: the managed
    /// wrapper's type is whatever the field was declared as, not the real native type,
    /// so the cast yields null for an object that genuinely is a RectTransform. Every
    /// "content or viewport missing" warning, and the silently skipped scroll range
    /// and row-height code, came from exactly this.
    /// </summary>
    private static RectTransform AsRect(Transform t)
    {
        if (t == null) return null;

        var rect = t.TryCast<RectTransform>();
        return rect != null ? rect : t.GetComponent<RectTransform>();
    }

    /// <summary>Viewport is the masked parent, never HouseScroller.rectTransform - that field is null here.</summary>
    private RectTransform Viewport => _anchor == null ? null : AsRect(_anchor.parent);

    private void DetectScrollReset()
    {
        if (!_scrollApplied || _reportedReset) return;

        var content = AsRect(_anchor);
        if (content == null) return;

        float actual = content.anchoredPosition.y;
        if (Mathf.Abs(actual - _expectedScrollY) < 1f) return;

        _reportedReset = true;
        Plugin.Trace.LogWarning(
            $"Scroll offset was overwritten: expected y={_expectedScrollY:0}, found {actual:0}. " +
            "Something else (most likely HouseScroller.Update) owns this transform.");
    }

    /// <summary>
    /// HouseScroller only advances on menu selection and hover thresholds, which a
    /// 200-row list cannot rely on - neither the wheel nor arrow keys move it. So the
    /// content is offset directly here, clamped to the overflow, which keeps working
    /// regardless of what the game's own scroller decides to do.
    /// </summary>
    private void HandleScrollWheel()
    {
        // Legacy Input may be inert in this build - the game drives its menus through
        // Rewired - so the wheel is a bonus path, not the one we rely on. The Up and
        // Down buttons use the same code and are guaranteed to work, because ordinary
        // button clicks already do everywhere else in this menu.
        float wheel;
        try { wheel = Input.mouseScrollDelta.y; }
        catch { return; }

        if (Mathf.Abs(wheel) < 0.01f) return;
        ScrollBy(-wheel * Plugin.Instance.ScrollSpeed.Value, "wheel");
    }

    /// <summary>
    /// Moves the list by <paramref name="delta"/> pixels, clamped to the overflow.
    /// Positive scrolls down. Logs unconditionally - if a scroll does nothing, the
    /// numbers here say whether the content is measured as too short, the viewport is
    /// wrong, or the offset is being applied and then reverted.
    /// </summary>
    private void ScrollBy(float delta, string source)
    {
        var content = AsRect(_anchor);
        var viewport = Viewport;

        if (content == null || viewport == null)
        {
            Plugin.Trace.LogWarning($"Scroll[{source}]: content or viewport missing.");
            return;
        }

        try
        {
            float overflow = Mathf.Max(0f, ContentHeight(content) - viewport.rect.height);

            _scrollY = Mathf.Clamp(_scrollY + delta, 0f, overflow);
            ApplyScroll(content);
        }
        catch (Exception e)
        {
            Plugin.Trace.LogWarning($"Scroll[{source}] failed: {e.Message}");
        }
    }

    /// <summary>
    /// The offset is owned here rather than read back from the transform, so a refresh
    /// that rebuilds every row cannot lose the player's place - and cannot leave the
    /// list pinned somewhere the new content no longer reaches.
    /// </summary>
    private float _scrollY;

    private void ApplyScroll(RectTransform content)
    {
        var pos = content.anchoredPosition;
        content.anchoredPosition = new Vector2(pos.x, _scrollY);
        _expectedScrollY = _scrollY;
        _scrollApplied = true;
    }

    /// <summary>Re-clamps and re-applies after the row set changes.</summary>
    private void RestoreScroll()
    {
        var content = AsRect(_anchor);
        var viewport = Viewport;
        if (content == null || viewport == null) return;

        try
        {
            float overflow = Mathf.Max(0f, ContentHeight(content) - viewport.rect.height);
            _scrollY = Mathf.Clamp(_scrollY, 0f, overflow);
            ApplyScroll(content);
        }
        catch (Exception e)
        {
            Plugin.Trace.LogWarning($"Restoring scroll failed: {e.Message}");
        }
    }

    /// <summary>One screenful, less a little overlap so nothing is skipped.</summary>
    private float PageSize()
    {
        var viewport = Viewport;
        var h = viewport == null ? 0f : viewport.rect.height;
        return h > 1f ? h * 0.8f : 400f;
    }

    // ---------------------------------------------------------------------- build

    private void Build()
    {
        _anchor = _menu.cardParent;
        if (_anchor == null) throw new InvalidOperationException("JoinMenu.cardParent is null.");

        var prefab = _menu.joinFriendCardPrefab;
        if (prefab == null) throw new InvalidOperationException("joinFriendCardPrefab is null.");

        _cardTemplate = prefab.transform;
        _buttonTemplate = _cardTemplate.Find("PlayButton");

        // Re-entering the menu calls OnEnable again, and comparing the JoinMenu
        // reference is not reliable - Il2CppInterop can hand out a fresh managed
        // wrapper for the same native object, so an identity check silently fails
        // and the section gets built a second time. Sweeping by name is independent
        // of all that, and also recovers if a previous build half-failed.
        ClearPreviousBuild();

        BuildHeader();
        BuildSearch();
        BuildControls();
        PinChrome();
    }

    /// <summary>
    /// Lifts the heading, search box and controls out of ScrollContents and onto the
    /// viewport, which does not move.
    ///
    /// Built inside the scrolling container they behaved like rows: they slid away as
    /// soon as you scrolled, and every sort re-clamped the list to the top just to
    /// reach them again. The viewport is the natural parent for chrome - the list
    /// scrolls underneath it - and ScrollContents gets top padding so the first rows
    /// are not hidden behind them.
    /// </summary>
    private void PinChrome()
    {
        var viewport = Viewport;
        if (viewport == null) return;

        float height = ButtonHeight();
        float y = 0f;

        foreach (var chrome in new[] { _header, _searchRow, _controls })
        {
            if (chrome == null) continue;

            chrome.transform.SetParent(viewport, worldPositionStays: false);

            var rect = AsRect(chrome.transform);
            if (rect == null) continue;

            // Stretched across the viewport, pinned to its top edge, stacking down.
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = new Vector2(0f, -y);

            chrome.transform.SetAsLastSibling();
            y += height;
        }

        _chromeHeight = y;
        ApplyListPadding();
    }

    private float _chromeHeight;
    private int _originalPaddingTop = int.MinValue;

    /// <summary>Reserves room for the pinned chrome, remembering what to put back.</summary>
    private void ApplyListPadding()
    {
        var group = _anchor == null ? null : _anchor.GetComponent<VerticalLayoutGroup>();
        if (group == null || group.padding == null) return;

        if (_originalPaddingTop == int.MinValue) _originalPaddingTop = group.padding.top;
        group.padding.top = _originalPaddingTop + Mathf.CeilToInt(_chromeHeight);

        LayoutRebuilder.MarkLayoutForRebuild(AsRect(_anchor));
    }

    /// <summary>The padding belongs to the game's own list, so it is handed back.</summary>
    private void RestoreListPadding()
    {
        if (_originalPaddingTop == int.MinValue) return;

        var group = _anchor == null ? null : _anchor.GetComponent<VerticalLayoutGroup>();
        if (group == null || group.padding == null) return;

        group.padding.top = _originalPaddingTop;
        _originalPaddingTop = int.MinValue;
        LayoutRebuilder.MarkLayoutForRebuild(AsRect(_anchor));
    }

    /// <summary>
    /// Search box, cloned from the join-code field so it gets the game's own input
    /// styling and caret behaviour. The clone carries the join-code field's persistent
    /// onValueChanged listeners, which would drive JoinMenu's connect logic from our
    /// typing - so they are cleared before ours is attached.
    /// </summary>
    private void BuildSearch()
    {
        var template = _menu.addressField;
        if (template == null)
        {
            Plugin.Trace.LogWarning("JoinMenu.addressField is null; search box omitted.");
            return;
        }

        _searchRow = NewRow("Search");
        if (_searchRow == null) return;

        var clone = UnityEngine.Object.Instantiate(template.gameObject, _searchRow.transform);
        clone.name = Prefix + "SearchField";
        clone.SetActive(true);

        var fit = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
        fit.flexibleWidth = 1f;
        fit.minWidth = 0f;
        fit.preferredHeight = ButtonHeight();

        _searchField = clone.GetComponent<TMP_InputField>();
        if (_searchField == null)
        {
            Plugin.Trace.LogWarning("Cloned search field has no TMP_InputField.");
            return;
        }

        _searchField.onValueChanged.RemoveAllListeners();
        _searchField.onEndEdit.RemoveAllListeners();
        _searchField.onSubmit.RemoveAllListeners();
        _searchField.text = "";
        _searchField.characterLimit = 32;

        if (_searchField.placeholder != null)
        {
            var ph = _searchField.placeholder.GetComponent<TMP_Text>();
            if (ph != null) ph.text = "Search worlds, hosts or codes...";
        }

        _searchField.onValueChanged.AddListener(
            DelegateSupport.ConvertDelegate<UnityAction<string>>(
                (Delegate)(Action<string>)(value =>
                {
                    _model.Query = value;
                    Rebuild();
                })));
    }

    /// <summary>
    /// A full-width row, built from a stripped card. A bare GameObject gets a default
    /// 100x100 RectTransform that the vertical layout never resizes, so its children
    /// run off the right edge and get cut by the mask.
    /// </summary>
    private GameObject NewRow(string name)
    {
        if (_cardTemplate == null) return null;

        var rowObject = UnityEngine.Object.Instantiate(_cardTemplate.gameObject, _anchor);
        rowObject.name = Prefix + name;
        rowObject.SetActive(true);

        var stock = rowObject.GetComponent<JoinFriendCard>();
        if (stock != null) UnityEngine.Object.DestroyImmediate(stock);

        for (int i = rowObject.transform.childCount - 1; i >= 0; i--)
            UnityEngine.Object.DestroyImmediate(rowObject.transform.GetChild(i).gameObject);

        var layout = rowObject.AddComponent<HorizontalLayoutGroup>();
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.spacing = 12f;
        layout.padding = new RectOffset(0, 0, 4, 4);

        var height = ButtonHeight();
        var size = rowObject.AddComponent<LayoutElement>();
        size.minHeight = height;
        size.preferredHeight = height;

        return rowObject;
    }

    private void ClearPreviousBuild()
    {
        _cards.Clear();
        _header = _controls = _searchRow = null;
        _searchField = null;
        _headerLabel = _refreshLabel = _sortLabel = _fullLabel = _crossplayLabel = default;

        // Sweep both parents: rows live under ScrollContents, while the chrome is
        // pinned to the viewport.
        var doomed = new List<GameObject>();
        foreach (var parent in new[] { _anchor, _anchor.parent })
        {
            if (parent == null) continue;

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == null) continue;
                if (child.name.StartsWith(Prefix, StringComparison.Ordinal) ||
                    child.name.StartsWith(CardPrefix, StringComparison.Ordinal))
                    doomed.Add(child.gameObject);
            }
        }

        foreach (var go in doomed) UnityEngine.Object.DestroyImmediate(go);

        if (doomed.Count > 0)
            Plugin.Trace.LogInfo($"Removed {doomed.Count} rows from a previous build.");
    }

    private void BuildHeader()
    {
        var friends = _anchor.Find("Join_Friends");
        if (friends == null)
        {
            Plugin.Trace.LogWarning("Join_Friends not found; the section will have no heading.");
            return;
        }

        _header = UnityEngine.Object.Instantiate(friends.gameObject, _anchor);
        _header.name = Prefix + "Heading";
        _header.SetActive(true);
        _headerLabel = UiLabel.InChildren(_header);
        _headerLabel.Set("PUBLIC LOBBIES");

        if (Plugin.Instance.CompactRows.Value)
            ScaleAllText(_header, Mathf.Clamp(Plugin.Instance.RowScale.Value, 0.3f, 1f));
    }

    /// <summary>
    /// One row holding refresh, sort and the filter toggles. This is the only object
    /// we build rather than clone, so it needs an explicit layout group - and its
    /// children need explicit widths, because a cloned PlayButton carries the card's
    /// full-width RectTransform and would otherwise overflow the mask.
    /// </summary>
    private void BuildControls()
    {
        if (_buttonTemplate == null)
        {
            Plugin.Trace.LogWarning("PlayButton not found on the card prefab; controls omitted.");
            return;
        }

        // Flexible widths under a full-width row: the four controls divide whatever
        // the card width is, so nothing overflows the mask at any resolution.
        _controls = NewRow("Controls");
        if (_controls == null) return;

        _refreshLabel = AddButton("Refresh", () =>
        {
            if (!_refresh.RequestManual())
                Plugin.Trace.LogInfo($"Refresh on cooldown ({_refresh.CooldownRemaining:0.0}s).");
        });

        _sortLabel = AddButton("Sort", () =>
        {
            _model.CycleSort();
            Rebuild();
        });

        _fullLabel = AddButton("Full", () =>
        {
            _model.HideFull = !_model.HideFull;
            Rebuild();
        });

        _crossplayLabel = AddButton("Crossplay", () =>
        {
            _model.CrossplayOnly = !_model.CrossplayOnly;
            Rebuild();
        });

        AddButton("Up", () => ScrollBy(-PageSize(), "button"));
        AddButton("Down", () => ScrollBy(PageSize(), "button"));
    }

    private float ButtonHeight()
    {
        var rect = AsRect(_buttonTemplate);
        var h = rect == null ? 0f : rect.rect.height;
        return h > 1f ? h : 48f;
    }

    private UiLabel AddButton(string name, Action onClick)
    {
        var clone = UnityEngine.Object.Instantiate(_buttonTemplate.gameObject, _controls.transform);
        clone.name = Prefix + name;
        clone.SetActive(true);

        var fit = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
        fit.flexibleWidth = 1f;
        fit.minWidth = 0f;
        fit.preferredWidth = -1f;
        fit.preferredHeight = ButtonHeight();

        Wire(clone.GetComponent<Button>(),
             () =>
             {
                 Plugin.Trace.LogInfo($"Clicked '{name}'.");
                 onClick();
             },
             name);

        if (Plugin.Instance.CompactRows.Value)
            ScaleAllText(clone, Mathf.Clamp(Plugin.Instance.RowScale.Value, 0.3f, 1f));

        var label = UiLabel.InChildren(clone);
        label.Set(name);

        // The card's Play label is right-aligned against the card edge; in a control
        // row that reads as a gap, so it is stretched and pulled back to the left.
        var text = clone.GetComponentInChildren<TMP_Text>(true);
        if (text != null)
        {
            text.alignment = TextAlignmentOptions.Left;

            var lr = text.rectTransform;
            lr.anchorMin = new Vector2(0f, 0f);
            lr.anchorMax = new Vector2(1f, 1f);
            lr.offsetMin = Vector2.zero;
            lr.offsetMax = Vector2.zero;
        }

        return label;
    }

    /// <summary>
    /// Attaches a click handler to a cloned button.
    ///
    /// The conversion is the whole point. Passing a managed Action straight to
    /// AddListener compiles - UnityEvent's parameter is an IL2CPP UnityAction and the
    /// implicit conversion is accepted - but the resulting listener is never invoked,
    /// which is indistinguishable from a dead button. DelegateSupport.ConvertDelegate
    /// builds a wrapper IL2CPP can actually call back through.
    ///
    /// Also guarantees a raycast target: a button whose graphic does not take
    /// raycasts never receives a pointer event in the first place.
    /// </summary>
    private static void Wire(Button button, Action onClick, string label = null)
    {
        if (button == null)
        {
            Plugin.Trace.LogWarning($"Wire({label}): no Button component.");
            return;
        }

        button.interactable = true;

        // RemoveAllListeners only clears runtime listeners. The cloned PlayButton also
        // carries a *persistent*, serialized listener pointing at JoinFriendCard.ActionJoin
        // - and we destroy JoinFriendCard on every clone. Clicking therefore invoked a
        // listener whose target no longer exists, threw NullReferenceException, and
        // UnityEvent abandoned the rest of the invocation list, so the handler added
        // below never ran. Silencing them is what makes these buttons clickable.
        int persistent = button.onClick.GetPersistentEventCount();
        for (int i = 0; i < persistent; i++)
            button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(
            DelegateSupport.ConvertDelegate<UnityAction>((Delegate)onClick));

        // A button whose graphic does not take raycasts never receives a pointer
        // event at all. Image specifically - GetComponent on the abstract Graphic
        // base is exactly the sort of thing that misbehaves across interop.
        var image = button.GetComponent<Image>();
        if (image == null) image = button.GetComponentInChildren<Image>(true);

        if (image != null)
        {
            image.raycastTarget = true;
            button.targetGraphic = image;
        }

        if (label != null)
            Plugin.Trace.LogInfo(
                $"Wire({label}): persistent={persistent} silenced " +
                $"image={(image != null ? image.name : "MISSING")} " +
                $"interactable={button.interactable}");
    }

    /// <summary>
    /// Multiplies every TMP label under <paramref name="root"/>, preserving the
    /// relative sizes the card was designed with.
    /// </summary>
    private static void ScaleAllText(GameObject root, float scale)
    {
        foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (label == null) continue;
            label.enableAutoSizing = false;
            label.fontSize *= scale;
        }
    }

    // -------------------------------------------------------------------- rebuild

    private void Rebuild()
    {
        if (!_built || _anchor == null) return;

        try
        {
            var rows = _model.Visible;

            // Rows are recycled, not rebuilt. Destroying and recreating them breaks
            // scrolling outright: HouseScroller is selection-driven, so once a refresh
            // destroys whatever the EventSystem had selected there is nothing left to
            // drive it, and the list freezes exactly one refresh after it starts.
            for (int i = 0; i < rows.Count; i++)
            {
                if (i < _cards.Count && _cards[i] != null)
                    ApplyEntry(_cards[i], rows[i]);
                else
                    _cards.Add(BuildCard(rows[i]));
            }

            // Surplus rows are hidden rather than destroyed, for the same reason.
            for (int i = rows.Count; i < _cards.Count; i++)
                if (_cards[i] != null) _cards[i].SetActive(false);

            Reorder();
            UpdateChrome();
            UpdateScrollRange();

            // Rows were destroyed and rebuilt; without this the list keeps whatever
            // transform offset it had against content that no longer matches, which
            // is what made scrolling stop working after the first refresh.
            RestoreScroll();
        }
        catch (Exception e)
        {
            if (!_reportedRebuildError)
            {
                _reportedRebuildError = true;
                Plugin.Trace.LogError($"Rebuild threw (further occurrences suppressed): {e}");
            }
        }
    }

    private bool _reportedRebuildError;

    /// <summary>
    /// The game appends friend cards to the same parent as friends come and go, so
    /// our rows are pushed to the end on every rebuild to keep the section contiguous
    /// and below the friends list.
    /// </summary>
    private void Reorder()
    {
        // Header, search and controls are no longer children of the list - they are
        // pinned to the viewport - so they are deliberately not reordered here.
        //
        // Only the rows currently in use; hidden pool entries stay where they are so
        // reordering does not disturb the layout with inactive objects.
        foreach (var card in _cards)
            if (card != null && card.activeSelf) card.transform.SetAsLastSibling();
    }

    /// <summary>
    /// HouseScroller.maxSteps is serialized for a friends list of a handful of rows,
    /// so without this the player simply cannot scroll far enough to see our cards.
    /// Recomputing from actual content height keeps the game's own scrolling - and
    /// therefore its controller and mouse-wheel handling - in charge.
    /// </summary>
    private void UpdateScrollRange()
    {
        var scroller = _menu?.scroller;
        if (scroller == null) return;

        try
        {
            var content = AsRect(_anchor);

            // Not scroller.rectTransform - that serialized field is null here, and
            // reading it is why this method silently returned before doing anything.
            var viewport = Viewport;
            if (content == null || viewport == null) return;

            float step = scroller.stepDistance;
            if (step <= 0.01f) return;

            float overflow = ContentHeight(content) - viewport.rect.height;
            int needed = overflow <= 0f ? 0 : Mathf.CeilToInt(overflow / step);

            Plugin.Trace.LogInfo(
                $"Scroll range: content={ContentHeight(content):0} viewport={viewport.rect.height:0} " +
                $"step={step:0.0} needed={needed} maxSteps={scroller.maxSteps} " +
                $"currentStep={scroller.currentStep} container={(scroller.containerTransform == null ? "null" : scroller.containerTransform.name)}");

            if (needed > scroller.maxSteps)
                scroller.maxSteps = needed;
        }
        catch (Exception e)
        {
            Plugin.Trace.LogWarning($"Could not extend the scroll range: {e.Message}");
        }
    }

    /// <summary>Creates a row. Only called when the pool needs to grow.</summary>
    private GameObject BuildCard(LobbyEntry entry)
    {
        var card = UnityEngine.Object.Instantiate(_cardTemplate.gameObject, _anchor);

        // The stock JoinFriendCard drives itself from the menu's friend list and
        // would fight us for the text fields, so the row becomes a plain widget.
        var stock = card.GetComponent<JoinFriendCard>();
        if (stock != null) UnityEngine.Object.DestroyImmediate(stock);

        // The friends list shows a handful of cards, so its display-size world name
        // is fine there. A public list is dozens of rows, and at full size only two
        // or three fit on screen - so the name is scaled down and the row tightened.
        Compact(card);

        ApplyEntry(card, entry);
        return card;
    }

    /// <summary>Points an existing row at a different lobby, reusing the widget.</summary>
    private void ApplyEntry(GameObject card, LobbyEntry entry)
    {
        card.name = CardPrefix + entry.JoinCode;
        if (!card.activeSelf) card.SetActive(true);

        UiLabel.At(card.transform, "GameName").Set(entry.WorldName);

        var host = string.IsNullOrEmpty(entry.HostName) ? "?" : entry.HostName;
        var detail = $"{host}   {entry.Occupancy}";
        if (!string.IsNullOrEmpty(entry.Platform)) detail += $"   {entry.Platform}";

        // Big Walk lobbies have no password field - the 6-digit join code is the only
        // secret. What a lobby can have is a non-public permission level, which is the
        // closest thing to "locked", so that is what gets marked rather than inventing
        // a password concept the game does not have.
        if (!entry.PubliclyAdvertised) detail += "   [LOCKED]";

        UiLabel.At(card.transform, "HostedBy/HostName").Set(detail);

        var play = card.transform.Find("PlayButton");
        var button = play == null ? null : play.GetComponent<Button>();
        if (button != null)
        {
            var code = entry.JoinCode;
            Wire(button, () => Join(code));
            button.interactable = !entry.IsFull;
        }
    }

    /// <summary>
    /// Scales a card down to list density. Font sizes are multiplied rather than set
    /// to absolutes so the card keeps its own typographic hierarchy - the world name
    /// stays larger than the host line, exactly as the friends list has it.
    /// </summary>
    private void Compact(GameObject card)
    {
        if (!Plugin.Instance.CompactRows.Value) return;

        var scale = Mathf.Clamp(Plugin.Instance.RowScale.Value, 0.3f, 1f);

        // Every label on the row, not just the world name - scaling one and not the
        // others inverts the card's hierarchy and looks broken at small sizes.
        ScaleAllText(card, scale);

        var height = CardHeight();
        if (height > 1f)
        {
            var fit = card.GetComponent<LayoutElement>() ?? card.AddComponent<LayoutElement>();
            fit.preferredHeight = height * scale;
            fit.minHeight = height * scale;
        }
    }

    /// <summary>
    /// Multiplies every TMP label under <paramref name="root"/>. Multiplying rather
    /// than assigning absolutes preserves the relative sizes the card was designed
    /// with, so the world name stays larger than the host line.
    /// </summary>
    private static void ScaleText(GameObject root, float scale)
    {
        foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (label == null) continue;
            label.enableAutoSizing = false;
            label.fontSize *= scale;
        }
    }

    private float CardHeight()
    {
        var rect = AsRect(_cardTemplate);
        return rect == null ? 0f : rect.rect.height;
    }

    private void Join(string joinCode)
    {
        if (_menu == null || string.IsNullOrEmpty(joinCode)) return;

        Plugin.Trace.LogInfo($"Joining lobby {joinCode}...");

        // ConnectTo is the same entry point the friend cards and the join-code box
        // use, so auth checks and error popups downstream behave normally.
        try { _menu.ConnectTo(joinCode); }
        catch (Exception e) { Plugin.Trace.LogError($"ConnectTo({joinCode}) threw: {e}"); }
    }

    private static void SetText(Transform root, string path, string value)
    {
        var t = root.Find(path);
        var tmp = t == null ? null : t.GetComponent<TextMeshProUGUI>();
        if (tmp != null) tmp.text = value;
    }

    private void UpdateChrome()
    {
        {
            string title;
            if (_refresh.IsSearching) title = "PUBLIC LOBBIES - SEARCHING";
            else if (_refresh.LastError != null) title = "PUBLIC LOBBIES - UNAVAILABLE";
            else if (!_refresh.HasSearched) title = "PUBLIC LOBBIES";
            else if (_model.TotalCount == 0) title = "PUBLIC LOBBIES - NONE FOUND";
            else if (_model.Visible.Count == _model.TotalCount) title = $"PUBLIC LOBBIES ({_model.TotalCount})";
            else title = $"PUBLIC LOBBIES ({_model.Visible.Count} OF {_model.TotalCount})";

            _headerLabel.Set(title);
        }

        {
            var cd = _refresh.CooldownRemaining;
            _refreshLabel.Set(_refresh.IsSearching ? "..."
                              : cd > 0f ? $"Refresh {cd:0}"
                              : "Refresh");
        }

        _sortLabel.Set(Label(_model.Sort));
        _fullLabel.Set(_model.HideFull ? "Full: hidden" : "Full: shown");
        _crossplayLabel.Set(_model.CrossplayOnly ? "Crossplay" : "All platforms");
    }

    private static string Label(LobbySort sort) => sort switch
    {
        LobbySort.MostPlayers => "Sort: busiest",
        LobbySort.FewestPlayers => "Sort: quietest",
        LobbySort.WorldName => "Sort: world",
        LobbySort.HostName => "Sort: host",
        LobbySort.Region => "Sort: region",
        _ => sort.ToString(),
    };
}
