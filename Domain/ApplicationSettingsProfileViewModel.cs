using System;
using System.Collections.Generic;
using System.Linq;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Mutable view-model consumed by the SettingsForm UI.
/// </summary>
public sealed class ApplicationSettingsProfileViewModel
{
    public HttpSettingsViewModel Http { get; set; } = new();
    public RelaySettingsViewModel Relay { get; set; } = new();
    public MatchSettingsViewModel Match { get; set; } = new();
    public PreflightSettingsViewModel Preflight { get; set; } = new();
    public UiAutomationSettingsViewModel UiAutomation { get; set; } = new();
    public DiagnosticsSettingsViewModel Diagnostics { get; set; } = new();

    public ApplicationSettingsProfileViewModel Clone()
    {
        return new ApplicationSettingsProfileViewModel
        {
            Http = Http.Clone(),
            Relay = Relay.Clone(),
            Match = Match.Clone(),
            Preflight = Preflight.Clone(),
            UiAutomation = UiAutomation.Clone(),
            Diagnostics = Diagnostics.Clone()
        };
    }

    public ApplicationSettingsStorage ToStorageModel()
    {
        return new ApplicationSettingsStorage
        {
            Http = Http.ToOptions(),
            Relay = Relay.ToOptions(),
            Match = Match.ToOptions(),
            Preflight = Preflight.ToOptions(),
            UiAutomation = UiAutomation.ToOptions(),
            Diagnostics = Diagnostics.ToOptions()
        };
    }

    public static ApplicationSettingsProfileViewModel FromOptions(
        HttpOptions http,
        RelayOptions relay,
        MatchOptions match,
        PreflightOptions preflight,
        UiAutomationOptions automation,
        DiagnosticsOptions diagnostics)
    {
        return new ApplicationSettingsProfileViewModel
        {
            Http = HttpSettingsViewModel.FromOptions(http),
            Relay = RelaySettingsViewModel.FromOptions(relay),
            Match = MatchSettingsViewModel.FromOptions(match),
            Preflight = PreflightSettingsViewModel.FromOptions(preflight),
            UiAutomation = UiAutomationSettingsViewModel.FromOptions(automation),
            Diagnostics = DiagnosticsSettingsViewModel.FromOptions(diagnostics)
        };
    }
}

public sealed class HttpSettingsViewModel
{
    public List<string> Urls { get; set; } = new();
    public string? BearerToken { get; set; }
    public List<string> AllowedCidrs { get; set; } = new();
    public int RequestTimeoutSeconds { get; set; } = 5;

    public HttpSettingsViewModel Clone() => new()
    {
        Urls = Urls.ToList(),
        BearerToken = BearerToken,
        AllowedCidrs = AllowedCidrs.ToList(),
        RequestTimeoutSeconds = RequestTimeoutSeconds
    };

    public HttpOptions ToOptions() => new()
    {
        Urls = Urls.ToArray(),
        BearerToken = string.IsNullOrWhiteSpace(BearerToken) ? null : BearerToken,
        AllowedCidrs = AllowedCidrs.ToArray(),
        RequestTimeoutSeconds = RequestTimeoutSeconds
    };

    public static HttpSettingsViewModel FromOptions(HttpOptions options) => new()
    {
        Urls = (options.Urls ?? Array.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).ToList(),
        BearerToken = options.BearerToken,
        AllowedCidrs = (options.AllowedCidrs ?? Array.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList(),
        RequestTimeoutSeconds = options.RequestTimeoutSeconds
    };
}

public sealed class RelaySettingsViewModel
{
    public bool Enabled { get; set; }
    public string? Url { get; set; }
    public string? BearerToken { get; set; }
    public bool EnableSchemaValidation { get; set; }

    public RelaySettingsViewModel Clone() => new()
    {
        Enabled = Enabled,
        Url = Url,
        BearerToken = BearerToken,
        EnableSchemaValidation = EnableSchemaValidation
    };

    public RelayOptions ToOptions() => new()
    {
        Enabled = Enabled,
        Url = string.IsNullOrWhiteSpace(Url) ? null : Url,
        BearerToken = string.IsNullOrWhiteSpace(BearerToken) ? null : BearerToken,
        EnableSchemaValidation = EnableSchemaValidation
    };

    public static RelaySettingsViewModel FromOptions(RelayOptions options) => new()
    {
        Enabled = options.Enabled,
        Url = options.Url,
        BearerToken = options.BearerToken,
        EnableSchemaValidation = options.EnableSchemaValidation
    };
}

public sealed class MatchSettingsViewModel
{
    public int LtDisplayedDurationSec { get; set; }
    public int AutoEndNoPlantAtSec { get; set; }
    public int DefuseWindowSec { get; set; }
    public int ClockExpectedHz { get; set; }
    public int LatencyWindow { get; set; }
    public int PreflightExpectedMatchLengthSec { get; set; }
    public int PropSessionTimeoutSeconds { get; set; }
    public int FinalDataTimeoutMs { get; set; }

    public MatchSettingsViewModel Clone() => new()
    {
        LtDisplayedDurationSec = LtDisplayedDurationSec,
        AutoEndNoPlantAtSec = AutoEndNoPlantAtSec,
        DefuseWindowSec = DefuseWindowSec,
        ClockExpectedHz = ClockExpectedHz,
        LatencyWindow = LatencyWindow,
        PreflightExpectedMatchLengthSec = PreflightExpectedMatchLengthSec,
        PropSessionTimeoutSeconds = PropSessionTimeoutSeconds,
        FinalDataTimeoutMs = FinalDataTimeoutMs
    };

    public MatchOptions ToOptions() => new()
    {
        LtDisplayedDurationSec = LtDisplayedDurationSec,
        AutoEndNoPlantAtSec = AutoEndNoPlantAtSec,
        DefuseWindowSec = DefuseWindowSec,
        ClockExpectedHz = ClockExpectedHz,
        LatencyWindow = LatencyWindow,
        PreflightExpectedMatchLengthSec = PreflightExpectedMatchLengthSec,
        PropSessionTimeoutSeconds = PropSessionTimeoutSeconds,
        FinalDataTimeoutMs = FinalDataTimeoutMs
    };

    public static MatchSettingsViewModel FromOptions(MatchOptions options) => new()
    {
        LtDisplayedDurationSec = options.LtDisplayedDurationSec,
        AutoEndNoPlantAtSec = options.AutoEndNoPlantAtSec,
        DefuseWindowSec = options.DefuseWindowSec,
        ClockExpectedHz = options.ClockExpectedHz,
        LatencyWindow = options.LatencyWindow,
        PreflightExpectedMatchLengthSec = options.PreflightExpectedMatchLengthSec,
        PropSessionTimeoutSeconds = options.PropSessionTimeoutSeconds,
        FinalDataTimeoutMs = options.FinalDataTimeoutMs
    };
}

public sealed class PreflightSettingsViewModel
{
    public bool Enabled { get; set; }
    public List<string> ExpectedTeamNames { get; set; } = new();
    public string ExpectedPlayerNamePattern { get; set; } = string.Empty;
    public bool EnforceMatchCancellation { get; set; }

    public PreflightSettingsViewModel Clone() => new()
    {
        Enabled = Enabled,
        ExpectedTeamNames = ExpectedTeamNames.ToList(),
        ExpectedPlayerNamePattern = ExpectedPlayerNamePattern,
        EnforceMatchCancellation = EnforceMatchCancellation
    };

    public PreflightOptions ToOptions() => new()
    {
        Enabled = Enabled,
        ExpectedTeamNames = ExpectedTeamNames.ToArray(),
        ExpectedPlayerNamePattern = ExpectedPlayerNamePattern,
        EnforceMatchCancellation = EnforceMatchCancellation
    };

    public static PreflightSettingsViewModel FromOptions(PreflightOptions options) => new()
    {
        Enabled = options.Enabled,
        ExpectedTeamNames = (options.ExpectedTeamNames ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList(),
        ExpectedPlayerNamePattern = options.ExpectedPlayerNamePattern,
        EnforceMatchCancellation = options.EnforceMatchCancellation
    };
}

public sealed class UiAutomationSettingsViewModel
{
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitleRegex { get; set; } = string.Empty;
    public int FocusTimeoutMs { get; set; }
    public int PostShortcutDelayMs { get; set; }
    public int DebounceWindowMs { get; set; }

    public UiAutomationSettingsViewModel Clone() => new()
    {
        ProcessName = ProcessName,
        WindowTitleRegex = WindowTitleRegex,
        FocusTimeoutMs = FocusTimeoutMs,
        PostShortcutDelayMs = PostShortcutDelayMs,
        DebounceWindowMs = DebounceWindowMs
    };

    public UiAutomationOptions ToOptions() => new()
    {
        ProcessName = ProcessName,
        WindowTitleRegex = WindowTitleRegex,
        FocusTimeoutMs = FocusTimeoutMs,
        PostShortcutDelayMs = PostShortcutDelayMs,
        DebounceWindowMs = DebounceWindowMs
    };

    public static UiAutomationSettingsViewModel FromOptions(UiAutomationOptions options) => new()
    {
        ProcessName = options.ProcessName ?? string.Empty,
        WindowTitleRegex = options.WindowTitleRegex ?? "^ICE$",
        FocusTimeoutMs = options.FocusTimeoutMs,
        PostShortcutDelayMs = options.PostShortcutDelayMs,
        DebounceWindowMs = options.DebounceWindowMs
    };
}

public sealed class DiagnosticsSettingsViewModel
{
    public string LogLevel { get; set; } = "Information";
    public bool WriteToFile { get; set; }
    public string LogPath { get; set; } = "logs/log-.txt";

    public DiagnosticsSettingsViewModel Clone() => new()
    {
        LogLevel = LogLevel,
        WriteToFile = WriteToFile,
        LogPath = LogPath
    };

    public DiagnosticsOptions ToOptions() => new()
    {
        LogLevel = LogLevel,
        WriteToFile = WriteToFile,
        LogPath = LogPath
    };

    public static DiagnosticsSettingsViewModel FromOptions(DiagnosticsOptions options) => new()
    {
        LogLevel = options.LogLevel,
        WriteToFile = options.WriteToFile,
        LogPath = options.LogPath
    };
}

public sealed class ApplicationSettingsStorage
{
    public HttpOptions? Http { get; set; }
    public RelayOptions? Relay { get; set; }
    public MatchOptions? Match { get; set; }
    public PreflightOptions? Preflight { get; set; }
    public UiAutomationOptions? UiAutomation { get; set; }
    public DiagnosticsOptions? Diagnostics { get; set; }
}
