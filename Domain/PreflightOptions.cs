namespace LaserTag.Defusal.Domain;

/// <summary>
/// Options controlling preflight validation rules surfaced in the UI.
/// </summary>
public sealed class PreflightOptions
{
    public bool Enabled { get; set; } = true;

    public string[] ExpectedTeamNames { get; set; } = ["Team 1", "Team 2"];

    public string ExpectedPlayerNamePattern { get; set; } = @"^Team\s(1|2)\s[A-Z]$";

    public bool EnforceMatchCancellation { get; set; }
}
