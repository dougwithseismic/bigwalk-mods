using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigWalk.VoiceSettings;

/// <summary>
/// Modifier-aware shortcut parser for the BepInEx 6 IL2CPP pack, which does not
/// include BepInEx 5's KeyboardShortcut configuration type.
/// </summary>
internal static class ShortcutBinding
{
    private readonly struct Parsed
    {
        public Parsed(KeyCode mainKey, KeyCode[] modifiers, string display)
        {
            MainKey = mainKey;
            Modifiers = modifiers;
            Display = display;
        }

        public KeyCode MainKey { get; }
        public KeyCode[] Modifiers { get; }
        public string Display { get; }
    }

    private static readonly HashSet<KeyCode> ModifierKeys = new HashSet<KeyCode>
    {
        KeyCode.LeftAlt, KeyCode.RightAlt,
        KeyCode.LeftControl, KeyCode.RightControl,
        KeyCode.LeftShift, KeyCode.RightShift,
        KeyCode.LeftCommand, KeyCode.RightCommand,
        KeyCode.LeftWindows, KeyCode.RightWindows,
    };

    public static bool IsDown(string value)
    {
        if (!TryParse(value, out var shortcut) || shortcut.MainKey == KeyCode.None) return false;
        if (!Input.GetKeyDown(shortcut.MainKey)) return false;

        foreach (KeyCode modifier in shortcut.Modifiers)
            if (!Input.GetKey(modifier)) return false;

        return true;
    }

    public static bool IsBound(string value)
    {
        return TryParse(value, out var shortcut) && shortcut.MainKey != KeyCode.None;
    }

    public static bool IsValid(string value)
    {
        return TryParse(value, out _);
    }

    public static string Display(string value)
    {
        if (!TryParse(value, out var shortcut)) return $"Invalid ({value})";
        return shortcut.MainKey == KeyCode.None ? "Not bound" : shortcut.Display;
    }

    public static bool Equivalent(string left, string right)
    {
        if (!TryParse(left, out var a) || !TryParse(right, out var b)) return false;
        return string.Equals(a.Display, b.Display, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParse(string value, out Parsed parsed)
    {
        parsed = new Parsed(KeyCode.None, Array.Empty<KeyCode>(), "Not bound");
        if (string.IsNullOrWhiteSpace(value)
            || value.Trim().Equals("Not bound", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("None", StringComparison.OrdinalIgnoreCase))
            return true;

        string[] tokens = value.Split('+', StringSplitOptions.RemoveEmptyEntries);
        var modifiers = new List<KeyCode>();
        KeyCode mainKey = KeyCode.None;

        foreach (string rawToken in tokens)
        {
            string token = ExpandAlias(rawToken.Trim());
            if (!Enum.TryParse(token, true, out KeyCode key) || key == KeyCode.None)
                return false;

            if (ModifierKeys.Contains(key))
            {
                if (!modifiers.Contains(key)) modifiers.Add(key);
            }
            else if (mainKey == KeyCode.None)
            {
                mainKey = key;
            }
            else
            {
                return false;
            }
        }

        if (mainKey == KeyCode.None) return false;

        modifiers.Sort((left, right) => ((int)left).CompareTo((int)right));
        var parts = new List<string>();
        foreach (KeyCode modifier in modifiers) parts.Add(modifier.ToString());
        parts.Add(mainKey.ToString());
        parsed = new Parsed(mainKey, modifiers.ToArray(), string.Join(" + ", parts));
        return true;
    }

    private static string ExpandAlias(string token)
    {
        if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase)) return nameof(KeyCode.LeftAlt);
        if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Control", StringComparison.OrdinalIgnoreCase)) return nameof(KeyCode.LeftControl);
        if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase)) return nameof(KeyCode.LeftShift);
        if (token.Equals("Cmd", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Command", StringComparison.OrdinalIgnoreCase)) return nameof(KeyCode.LeftCommand);
        return token;
    }
}
