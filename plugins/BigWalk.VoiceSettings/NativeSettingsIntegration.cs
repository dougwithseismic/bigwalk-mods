using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace BigWalk.VoiceSettings;

/// <summary>
/// Extends Big Walk's existing Audio category with native-styled voice controls.
/// Rows are cloned from the live menu so fonts, animation, sounds, and controller
/// selection remain consistent with the version of the game being played.
/// </summary>
internal static class NativeSettingsIntegration
{
    private const string RootName = "BigWalk.VoiceSettings.Native.Positioning";
    private const string PositioningName = "BigWalk.VoiceSettings.Native.Positioning";
    private const string RangeName = "BigWalk.VoiceSettings.Native.Range";
    private const string IndicatorsName = "BigWalk.VoiceSettings.Native.Indicators";
    private const string AdvancedName = "BigWalk.VoiceSettings.Native.Advanced";

    private static readonly float[] RangePresets = { 1f, 2f, 5f, 10f, 20f };
    private static readonly string[] RangeLabels = { "VANILLA ×1", "SOCIAL ×2", "WIDE ×5", "FAR ×10", "MAXIMUM ×20" };
    private static readonly Dictionary<int, AudioScrollState> Scrollers = new();

    public static void Ensure()
    {
        try
        {
            foreach (SettingsMenu menu in UnityEngine.Object.FindObjectsOfType<SettingsMenu>(true))
                Attach(menu);
        }
        catch (Exception exception)
        {
            Plugin.Trace.LogError($"Could not attach native Audio settings: {exception}");
        }
    }

    public static void Refresh()
    {
        foreach (SettingsMenu menu in UnityEngine.Object.FindObjectsOfType<SettingsMenu>(true))
            Refresh(menu);
    }

    public static void Tick()
    {
        foreach (AudioScrollState scroller in Scrollers.Values)
            scroller.Tick();
    }

    private static void Attach(SettingsMenu menu)
    {
        SettingsCatagory audio = menu?.catagoryAudio;
        if (audio?.rows == null || audio.rows.Length == 0) return;

        Transform existing = FindDescendant(menu.transform, RootName);
        if (existing != null)
        {
            EnsureScroller(menu, existing.parent);
            Refresh(menu);
            return;
        }

        SettingsRow cycleTemplate = FindCycleTemplate(audio);
        if (cycleTemplate == null) return;
        SettingsRow sliderTemplate = FindSliderTemplate(audio);

        Transform parent = cycleTemplate.transform.parent;
        int sibling = LastAudioSibling(audio) + 1;
        var customRows = new List<SettingsRow>();

        SettingsRow positioning = CloneRow(cycleTemplate, parent, sibling++, PositioningName);
        ConfigureCycleRow(positioning, "VOICE POSITIONING", () => Plugin.Instance.TwoDVoice.Value ? "2D EVERYWHERE" : "3D SPATIAL",
            delta =>
            {
                Plugin.Instance.TwoDVoice.Value = !Plugin.Instance.TwoDVoice.Value;
                ApplyAndRefresh();
            });
        customRows.Add(positioning);

        SettingsRow range = CloneRow(sliderTemplate ?? cycleTemplate, parent, sibling++, RangeName);
        if (range.slider != null)
        {
            ConfigureSliderRow(range, "PROXIMITY RANGE", Plugin.Instance.RangeMultiplier.Value,
                value =>
                {
                    Plugin.Instance.RangeMultiplier.Value = value;
                    ApplyAndRefresh();
                });
        }
        else
        {
            ConfigureCycleRow(range, "PROXIMITY RANGE", RangeLabel,
                delta =>
                {
                    int index = ClosestRangeIndex(Plugin.Instance.RangeMultiplier.Value);
                    index = (index + delta + RangePresets.Length) % RangePresets.Length;
                    Plugin.Instance.RangeMultiplier.Value = RangePresets[index];
                    ApplyAndRefresh();
                });
        }
        customRows.Add(range);

        SettingsRow indicators = CloneRow(cycleTemplate, parent, sibling++, IndicatorsName);
        ConfigureCycleRow(indicators, "SPEAKER INDICATORS", IndicatorLabel,
            delta =>
            {
                int index = IndicatorIndex();
                index = (index + delta + 3) % 3;
                SetIndicatorIndex(index);
                Refresh();
            });
        customRows.Add(indicators);

        SettingsRow buttonTemplate = FindButtonTemplate(menu);
        SettingsRow advanced = buttonTemplate != null
            ? CloneRow(buttonTemplate, parent, sibling, AdvancedName)
            : CloneRow(cycleTemplate, parent, sibling, AdvancedName);

        if (advanced.boringButton != null)
        {
            ConfigureButtonRow(advanced, "BETTER PROXIMITY VOICE", "ADVANCED SETTINGS",
                () => Plugin.Instance.Overlay.OpenFromNativeSettings(menu));
        }
        else
        {
            ConfigureCycleRow(advanced, "ADVANCED VOICE SETTINGS", () => "OPEN",
                _ => Plugin.Instance.Overlay.OpenFromNativeSettings(menu));
        }
        customRows.Add(advanced);

        RebuildNavigation(audio, customRows);
        RectTransform rect = AsRect(parent);
        if (rect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

        EnsureScroller(menu, parent);

        Refresh(menu);
        Plugin.Trace.LogInfo($"Added native voice controls to {(menu.isInMainMenu ? "main-menu" : "in-game")} Audio settings.");
    }

    private static SettingsRow FindCycleTemplate(SettingsCatagory category)
    {
        foreach (SettingsRow row in category.rows)
            if (row != null && row.leftButton != null && row.rightButton != null && row.arrayLabel != null)
                return row;
        return null;
    }

    private static SettingsRow FindSliderTemplate(SettingsCatagory category)
    {
        foreach (SettingsRow row in category.rows)
            if (row != null && row.slider != null && row.sliderLabel != null)
                return row;
        return null;
    }

    private static SettingsRow FindButtonTemplate(SettingsMenu menu)
    {
        SettingsCatagory[] categories =
        {
            menu.catagoryAudio,
            menu.catagoryGeneral,
            menu.catagoryControls,
            menu.catagoryGraphics,
        };

        foreach (SettingsCatagory category in categories)
        {
            if (category?.rows == null) continue;
            foreach (SettingsRow row in category.rows)
                if (row != null && row.boringButton != null)
                    return row;
        }

        return null;
    }

    private static int LastAudioSibling(SettingsCatagory category)
    {
        int result = 0;
        foreach (SettingsRow row in category.rows)
            if (row != null) result = Math.Max(result, row.transform.GetSiblingIndex());
        return result;
    }

    private static SettingsRow CloneRow(SettingsRow template, Transform parent, int sibling, string name)
    {
        GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, parent);
        clone.name = name;
        clone.transform.SetSiblingIndex(sibling);
        clone.SetActive(true);

        SettingsRow row = clone.GetComponent<SettingsRow>();
        row.enabled = false;
        row.settingsMenu = template.settingsMenu;
        return row;
    }

    private static void ConfigureCycleRow(SettingsRow row, string title, Func<string> value, Action<int> change)
    {
        SetRowTitle(row, title);
        SetRaw(row.arrayLabel, value());
        Wire(row.leftButton, () => change(-1));
        Wire(row.rightButton, () => change(1));
    }

    private static void ConfigureButtonRow(SettingsRow row, string title, string buttonText, Action action)
    {
        SetRowTitle(row, title);
        SetButtonText(row.boringButton, buttonText);
        Wire(row.boringButton, action);
    }

    private static void ConfigureSliderRow(SettingsRow row, string title, float value, Action<float> change)
    {
        SetRowTitle(row, title);
        row.slider.minValue = 1f;
        row.slider.maxValue = VoiceRangeScaler.MaxScale;
        row.slider.wholeNumbers = true;
        row.slider.SetValueWithoutNotify(Mathf.Round(value));
        SetRaw(row.sliderLabel, $"×{Mathf.RoundToInt(value)}");

        int persistent = row.slider.onValueChanged.GetPersistentEventCount();
        for (int index = 0; index < persistent; index++)
            row.slider.onValueChanged.SetPersistentListenerState(index, UnityEventCallState.Off);
        row.slider.onValueChanged.RemoveAllListeners();
        row.slider.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<float>>(
            (Delegate)(Action<float>)change));
    }

    private static void Wire(Button button, Action callback)
    {
        if (button == null) return;
        int persistent = button.onClick.GetPersistentEventCount();
        for (int index = 0; index < persistent; index++)
            button.onClick.SetPersistentListenerState(index, UnityEventCallState.Off);
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Delegate)callback));
    }

    private static void Refresh(SettingsMenu menu)
    {
        if (menu == null) return;
        SetCycleTitle(menu.transform, PositioningName, "VOICE POSITIONING");
        SetCycleValue(menu.transform, PositioningName,
            Plugin.Instance.TwoDVoice.Value ? "2D EVERYWHERE" : "3D SPATIAL");
        SetCycleTitle(menu.transform, RangeName, "PROXIMITY RANGE");
        SetRangeValue(menu.transform);
        SetCycleTitle(menu.transform, IndicatorsName, "SPEAKER INDICATORS");
        SetCycleValue(menu.transform, IndicatorsName, IndicatorLabel());
        SetCycleTitle(menu.transform, AdvancedName, "BETTER PROXIMITY VOICE");

        Transform advanced = FindDescendant(menu.transform, AdvancedName);
        SettingsRow advancedRow = advanced?.GetComponent<SettingsRow>();
        if (advancedRow?.boringButton != null)
            SetButtonText(advancedRow.boringButton, "ADVANCED SETTINGS");
        else if (advancedRow?.arrayLabel != null)
            SetRaw(advancedRow.arrayLabel, "OPEN");
    }

    private static void SetCycleTitle(Transform menu, string name, string value)
    {
        Transform transform = FindDescendant(menu, name);
        SettingsRow row = transform?.GetComponent<SettingsRow>();
        if (row == null) return;
        SetRowTitle(row, value);
    }

    private static void SetCycleValue(Transform menu, string name, string value)
    {
        Transform transform = FindDescendant(menu, name);
        SettingsRow row = transform?.GetComponent<SettingsRow>();
        if (row?.arrayLabel != null) SetRaw(row.arrayLabel, value);
    }

    private static void SetRangeValue(Transform menu)
    {
        Transform transform = FindDescendant(menu, RangeName);
        SettingsRow row = transform?.GetComponent<SettingsRow>();
        if (row == null) return;

        if (row.slider != null)
        {
            float value = Mathf.Clamp(Plugin.Instance.RangeMultiplier.Value, 1f, VoiceRangeScaler.MaxScale);
            row.slider.SetValueWithoutNotify(Mathf.Round(value));
            SetRaw(row.sliderLabel, $"×{value:0.##}");
        }
        else if (row.arrayLabel != null)
        {
            SetRaw(row.arrayLabel, RangeLabel());
        }
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        if (root == null) return null;
        var transforms = root.GetComponentsInChildren<Transform>(true);
        foreach (Transform transform in transforms)
            if (transform != null && transform.gameObject.name == name)
                return transform;
        return null;
    }

    private static void RebuildNavigation(SettingsCatagory category, List<SettingsRow> customRows)
    {
        var rows = new List<SettingsRow>();
        foreach (SettingsRow row in category.rows)
            if (row != null) rows.Add(row);
        rows.AddRange(customRows);

        for (int index = 0; index < rows.Count; index++)
        {
            SettingsRow previous = index == 0 ? null : rows[index - 1];
            SettingsRow next = index == rows.Count - 1 ? null : rows[index + 1];
            rows[index].SetNavigation(previous, next);
        }
    }

    private static void ApplyAndRefresh()
    {
        Plugin.Instance.Overlay.ApplyImmediately();
        Refresh();
    }

    private static string RangeLabel()
    {
        float value = Plugin.Instance.RangeMultiplier.Value;
        int index = ClosestRangeIndex(value);
        return Mathf.Abs(RangePresets[index] - value) < 0.01f
            ? RangeLabels[index]
            : $"CUSTOM ×{value:0.##}";
    }

    private static int ClosestRangeIndex(float value)
    {
        int best = 0;
        float distance = float.MaxValue;
        for (int index = 0; index < RangePresets.Length; index++)
        {
            float candidate = Mathf.Abs(RangePresets[index] - value);
            if (candidate >= distance) continue;
            distance = candidate;
            best = index;
        }
        return best;
    }

    private static int IndicatorIndex()
    {
        if (!Plugin.Instance.SpeakerIndicators.Value) return 0;
        return Plugin.Instance.ActiveSpeakerRoster.Value ? 2 : 1;
    }

    private static string IndicatorLabel()
    {
        return IndicatorIndex() switch
        {
            0 => "OFF",
            1 => "ICONS",
            _ => "ICONS + ROSTER",
        };
    }

    private static void SetIndicatorIndex(int index)
    {
        Plugin.Instance.SpeakerIndicators.Value = index != 0;
        Plugin.Instance.ActiveSpeakerRoster.Value = index == 2;
    }

    private static void SetRaw(LocalizedText localized, string value)
    {
        if (localized == null) return;
        localized.displayType = LocalizedText.DisplayType.RawValue;
        localized.rawValue = value;
        localized.Change(value, LocalizedText.DisplayType.RawValue);

        // Some cloned LocalizedText instances retain the template key and can write
        // it back after Change(). Setting the rendered TMP object as well makes our
        // raw labels authoritative even during a localization refresh.
        if (localized.textElement != null)
            localized.textElement.text = value;
        foreach (TMP_Text text in localized.GetComponentsInChildren<TMP_Text>(true))
            if (text != null) text.text = value;
    }

    /// <summary>
    /// SettingsRow.title is not consistently wired to the TMP object rendered by
    /// every Audio-row prefab. Find the visual heading from the complete cloned
    /// row while excluding the row's value and interactive-control labels.
    /// </summary>
    private static void SetRowTitle(SettingsRow row, string value)
    {
        if (row == null) return;
        SetRaw(row.title, value);

        TMP_Text arrayValue = row.arrayLabel?.textElement;
        TMP_Text sliderValue = row.sliderLabel?.textElement;
        foreach (TMP_Text text in row.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text == null || text == arrayValue || text == sliderValue) continue;
            if (IsWithin(text.transform, row.leftButton?.transform) ||
                IsWithin(text.transform, row.rightButton?.transform) ||
                IsWithin(text.transform, row.boringButton?.transform))
                continue;

            LocalizedText localized = text.GetComponent<LocalizedText>();
            if (localized != null)
            {
                localized.enabled = false;
                localized.displayType = LocalizedText.DisplayType.RawValue;
                localized.rawValue = value;
            }

            text.text = value;
        }
    }

    private static bool IsWithin(Transform candidate, Transform ancestor)
    {
        if (candidate == null || ancestor == null) return false;
        Transform current = candidate;
        while (current != null)
        {
            if (current == ancestor) return true;
            current = current.parent;
        }

        return false;
    }

    private static void SetButtonText(Button button, string value)
    {
        TMP_Text text = button?.GetComponentInChildren<TMP_Text>(true);
        if (text == null) return;
        LocalizedText localized = text.GetComponent<LocalizedText>();
        if (localized != null) SetRaw(localized, value);
        else text.text = value;
    }

    private static void EnsureScroller(SettingsMenu menu, Transform contentTransform)
    {
        int key = menu.GetInstanceID();
        if (Scrollers.ContainsKey(key)) return;

        RectTransform content = AsRect(contentTransform);
        RectTransform viewport = AsRect(menu.contents);
        if (viewport == null || viewport == content)
            viewport = AsRect(contentTransform?.parent);
        if (content == null || viewport == null || viewport == content) return;

        Scrollers[key] = new AudioScrollState(menu, content, viewport);
    }

    private static RectTransform AsRect(Transform transform)
    {
        if (transform == null) return null;
        RectTransform rect = transform.TryCast<RectTransform>();
        return rect != null ? rect : transform.GetComponent<RectTransform>();
    }

    internal static bool IsCustomRow(SettingsRow row)
    {
        return row != null && row.gameObject.name.StartsWith("BigWalk.VoiceSettings.Native.", StringComparison.Ordinal);
    }

    internal static void ResetToDefaults()
    {
        Plugin.Instance.RangeMultiplier.Value = 1f;
        Plugin.Instance.TwoDVoice.Value = false;
        Plugin.Instance.SpeakerIndicators.Value = true;
        Plugin.Instance.SpeakerNames.Value = true;
        Plugin.Instance.ActiveSpeakerRoster.Value = true;
        Plugin.Instance.SpeakerDistances.Value = true;
        Plugin.Instance.HudScale.Value = 1f;
        Plugin.Instance.TalkThreshold.Value = 0.004f;
        Plugin.Instance.Overlay.ApplyImmediately();
        Refresh();
    }

    private sealed class AudioScrollState
    {
        private const float EdgePadding = 18f;

        private readonly SettingsMenu _menu;
        private readonly RectTransform _content;
        private readonly RectTransform _viewport;
        private readonly ScrollRect _scrollRect;
        private readonly bool _ownsScrollRect;
        private bool _wasActive;
        private bool _reportedTickError;
        private GameObject _lastSelection;

        public AudioScrollState(SettingsMenu menu, RectTransform content, RectTransform viewport)
        {
            _menu = menu;
            _content = content;
            _viewport = viewport;

            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (viewport.GetComponent<RectMask2D>() == null && viewport.GetComponent<Mask>() == null)
                viewport.gameObject.AddComponent<RectMask2D>();

            // ScrollRect receives pointer-wheel and drag events only when a Graphic is
            // hit. Stock Settings has graphics on its buttons but not on the empty
            // panel, which made scrolling appear to work only while hovering a button.
            // A fully transparent parent graphic gives the entire viewport a hit
            // surface; child buttons remain later/deeper raycast targets and keep
            // their normal click and hover behaviour.
            Image hitSurface = viewport.GetComponent<Image>();
            if (hitSurface == null) hitSurface = viewport.gameObject.AddComponent<Image>();
            hitSurface.color = new Color(0f, 0f, 0f, 0f);
            hitSurface.raycastTarget = true;

            _scrollRect = viewport.GetComponent<ScrollRect>();
            if (_scrollRect == null)
            {
                _scrollRect = viewport.gameObject.AddComponent<ScrollRect>();
                _ownsScrollRect = true;
            }

            _scrollRect.content = content;
            _scrollRect.viewport = viewport;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.inertia = true;
            _scrollRect.decelerationRate = 0.135f;
            _scrollRect.scrollSensitivity = 72f;

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            Canvas.ForceUpdateCanvases();
            _scrollRect.verticalNormalizedPosition = 1f;

            Plugin.Trace.LogInfo(
                $"Audio scrolling enabled: content '{content.gameObject.name}' {content.rect.height:0}px, " +
                $"viewport '{viewport.gameObject.name}' {viewport.rect.height:0}px.");
        }

        public void Tick()
        {
            try
            {
                if (_menu == null || _content == null || _viewport == null) return;
                bool active = _menu.isActiveAndEnabled && _menu.activeCatagory == _menu.catagoryAudio;

                if (_ownsScrollRect) _scrollRect.enabled = active;
                if (!active)
                {
                    _wasActive = false;
                    return;
                }

                if (!_wasActive)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
                    Canvas.ForceUpdateCanvases();
                    _scrollRect.verticalNormalizedPosition = 1f;
                    _wasActive = true;
                }

                KeepSelectionVisible();
            }
            catch (Exception exception)
            {
                if (_reportedTickError) return;
                _reportedTickError = true;
                Plugin.Trace.LogWarning($"Audio scrolling update failed (further occurrences suppressed): {exception.Message}");
            }
        }

        private void KeepSelectionVisible()
        {
            GameObject selected = EventSystem.current?.currentSelectedGameObject;
            if (selected == null || selected == _lastSelection) return;

            Transform ancestor = selected.transform;
            bool belongsToContent = false;
            while (ancestor != null)
            {
                if (ancestor == _content)
                {
                    belongsToContent = true;
                    break;
                }
                ancestor = ancestor.parent;
            }
            if (!belongsToContent) return;
            _lastSelection = selected;

            RectTransform selectedRect = selected.GetComponent<RectTransform>();
            if (selectedRect == null) return;

            float selectedScale = Mathf.Abs(selectedRect.lossyScale.y);
            float viewportScale = Mathf.Abs(_viewport.lossyScale.y);
            float selectedHalfHeight = selectedRect.rect.height * selectedScale * 0.5f;
            float viewportHalfHeight = _viewport.rect.height * viewportScale * 0.5f;
            float selectedBottom = selectedRect.position.y - selectedHalfHeight;
            float selectedTop = selectedRect.position.y + selectedHalfHeight;
            float viewportBottom = _viewport.position.y - viewportHalfHeight + EdgePadding;
            float viewportTop = _viewport.position.y + viewportHalfHeight - EdgePadding;
            Vector2 anchored = _content.anchoredPosition;

            if (selectedBottom < viewportBottom)
                anchored.y += (viewportBottom - selectedBottom) / Mathf.Max(selectedScale, 0.001f);
            else if (selectedTop > viewportTop)
                anchored.y -= (selectedTop - viewportTop) / Mathf.Max(selectedScale, 0.001f);
            else
                return;

            float overflow = Mathf.Max(0f, LayoutUtility.GetPreferredHeight(_content) - _viewport.rect.height);
            anchored.y = Mathf.Clamp(anchored.y, 0f, overflow);
            _content.anchoredPosition = anchored;
            _scrollRect.StopMovement();
        }
    }
}

[HarmonyPatch(typeof(SettingsMenu), nameof(SettingsMenu.ActionResetAudio))]
internal static class AudioResetPatch
{
    private static void Postfix()
    {
        NativeSettingsIntegration.ResetToDefaults();
    }
}

[HarmonyPatch(typeof(SettingsRow), nameof(SettingsRow.Refresh))]
internal static class CustomSettingsRowRefreshPatch
{
    private static bool Prefix(SettingsRow __instance)
    {
        if (!NativeSettingsIntegration.IsCustomRow(__instance)) return true;
        NativeSettingsIntegration.Refresh();
        return false;
    }
}
