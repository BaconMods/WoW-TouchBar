namespace WoWTouchBar.Core.TouchBar;

/// <summary>
/// State of the game → companion link, as the bridge sees it. Stale = the strip has not been readable for a few
/// seconds (data greyed out). Whether WoW has focus is passed separately (it picks WoW pages vs the system page).
/// </summary>
public enum LinkStatus { WaitingForWow, LookingForAddon, Connected, Stale, NoSignal, AddonIncompatible }
