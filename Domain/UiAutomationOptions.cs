namespace LaserTag.Defusal.Domain;

/// <summary>
/// Options governing the automation steps required to end a match.
/// </summary>
public sealed class UiAutomationOptions
{
    public string ProcessName { get; set; } = "ICombat.Desktop";

    public string WindowTitleRegex { get; set; } = "^ICE$";

    public int FocusTimeoutMs { get; set; } = 1500;

    public int PostShortcutDelayMs { get; set; } = 150;

    public int DebounceWindowMs { get; set; } = 2000;
}
