namespace LaserTag.Defusal.Domain;

/// <summary>
/// Options controlling diagnostic logging behaviour.
/// </summary>
public sealed class DiagnosticsOptions
{
    public string LogLevel { get; set; } = "Information";

    public bool WriteToFile { get; set; } = true;

    public string LogPath { get; set; } = "logs/log-.txt";
}
