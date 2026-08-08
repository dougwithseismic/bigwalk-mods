using TMPro;
using UnityEngine;

namespace BigWalk.PublicLobbies;

/// <summary>
/// A text element on a cloned widget, written through whichever component actually
/// owns it.
///
/// Most of the game's labels carry a LocalizedText, which rewrites its TMP_Text from
/// a localisation key whenever LocalizedText.RefreshAll() runs. Assigning TMP_Text
/// directly therefore only holds until the next refresh.
///
/// Destroying LocalizedText is not the answer either: instances subscribe to its
/// *static* onRefresh event, and a destroyed-but-still-subscribed instance makes
/// RefreshAll() throw a NullReferenceException every time it fires. Instead the
/// component is told to display a raw value, which is a supported mode and leaves it
/// healthy and subscribed.
/// </summary>
public readonly struct UiLabel
{
    private readonly LocalizedText _localized;
    private readonly TMP_Text _text;

    private UiLabel(LocalizedText localized, TMP_Text text)
    {
        _localized = localized;
        _text = text;
    }

    public bool Exists => _localized != null || _text != null;

    /// <summary>Wraps the label components on <paramref name="go"/> itself.</summary>
    public static UiLabel On(GameObject go)
    {
        if (go == null) return default;
        return new UiLabel(go.GetComponent<LocalizedText>(), go.GetComponent<TMP_Text>());
    }

    /// <summary>Wraps the first label found anywhere under <paramref name="root"/>.</summary>
    public static UiLabel InChildren(GameObject root)
    {
        if (root == null) return default;

        var text = root.GetComponentInChildren<TMP_Text>(true);
        if (text == null) return default;

        return new UiLabel(text.GetComponent<LocalizedText>(), text);
    }

    public static UiLabel At(Transform root, string path)
    {
        var child = root == null ? null : root.Find(path);
        return child == null ? default : On(child.gameObject);
    }

    public void Set(string value)
    {
        if (_localized != null)
        {
            _localized.Change(value, LocalizedText.DisplayType.RawValue);
            return;
        }

        if (_text != null) _text.text = value;
    }

    /// <summary>Multiplies the font size, preserving the card's own hierarchy.</summary>
    public void Scale(float scale)
    {
        if (_text == null) return;
        _text.enableAutoSizing = false;
        _text.fontSize *= scale;
    }
}
