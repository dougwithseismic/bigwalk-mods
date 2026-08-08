using System;
using UnityEngine;

namespace BigWalk.PublicLobbies;

/// <summary>
/// A DontDestroyOnLoad component whose only job is to give the browser a frame tick.
///
/// NativeLobbyBrowser is a plain object rather than a MonoBehaviour - it is built and
/// discarded with the JoinMenu, which has its own lifecycle - so it borrows an Update
/// from here instead of owning a GameObject.
/// </summary>
public class BrowserHost : MonoBehaviour
{
    public BrowserHost(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        // Tick is already guarded internally; this catch is the last line of defence
        // so a bad frame cannot spam Unity's log from an Update we injected.
        try { Plugin.Browser?.Tick(); }
        catch (Exception e) { Plugin.Trace.LogError($"Browser tick threw: {e}"); }
    }
}
