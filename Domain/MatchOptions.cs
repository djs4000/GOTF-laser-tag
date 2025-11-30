namespace LaserTag.Defusal.Domain;

/// <summary>
/// Options describing the match timing rules.
/// </summary>
public sealed class MatchOptions
{
    public int LtDisplayedDurationSec { get; set; } = 219;

    public int AutoEndNoPlantAtSec { get; set; } = 180;

    public int DefuseWindowSec { get; set; } = 40;

    public int ClockExpectedHz { get; set; } = 10;

    public int PreflightExpectedMatchLengthSec { get; set; } = 219;

    public int PropSessionTimeoutSeconds { get; set; } = 15;
}
