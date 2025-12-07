using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LaserTag.Defusal.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaserTag.Defusal.Services;

/// <summary>
/// Handles loading and saving of appsettings.json through strongly typed view-models.
/// </summary>
public sealed class SettingsPersistenceService
{
    private readonly ILogger<SettingsPersistenceService> _logger;
    private readonly IOptionsMonitor<HttpOptions> _httpOptions;
    private readonly IOptionsMonitor<RelayOptions> _relayOptions;
    private readonly IOptionsMonitor<MatchOptions> _matchOptions;
    private readonly IOptionsMonitor<PreflightOptions> _preflightOptions;
    private readonly IOptionsMonitor<UiAutomationOptions> _uiAutomationOptions;
    private readonly IOptionsMonitor<DiagnosticsOptions> _diagnosticsOptions;
    private readonly JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };
    private readonly string _configPath;

    public SettingsPersistenceService(
        ILogger<SettingsPersistenceService> logger,
        IOptionsMonitor<HttpOptions> httpOptions,
        IOptionsMonitor<RelayOptions> relayOptions,
        IOptionsMonitor<MatchOptions> matchOptions,
        IOptionsMonitor<PreflightOptions> preflightOptions,
        IOptionsMonitor<UiAutomationOptions> uiAutomationOptions,
        IOptionsMonitor<DiagnosticsOptions> diagnosticsOptions)
    {
        _logger = logger;
        _httpOptions = httpOptions;
        _relayOptions = relayOptions;
        _matchOptions = matchOptions;
        _preflightOptions = preflightOptions;
        _uiAutomationOptions = uiAutomationOptions;
        _diagnosticsOptions = diagnosticsOptions;
        _configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }

    public async Task<ApplicationSettingsProfileViewModel> LoadAsync(CancellationToken cancellationToken)
    {
        var storage = await ReadStorageAsync(cancellationToken).ConfigureAwait(false)
                      ?? BuildStorageFromOptions();
        _logger.LogInformation("Loaded application settings profile from {Path}", _configPath);
        return ApplicationSettingsProfileViewModel.FromOptions(
            storage.Http ?? _httpOptions.CurrentValue,
            storage.Relay ?? _relayOptions.CurrentValue,
            storage.Match ?? _matchOptions.CurrentValue,
            storage.Preflight ?? _preflightOptions.CurrentValue,
            storage.UiAutomation ?? _uiAutomationOptions.CurrentValue,
            storage.Diagnostics ?? _diagnosticsOptions.CurrentValue);
    }

    public IReadOnlyList<ValidationIssue> Validate(ApplicationSettingsProfileViewModel profile)
    {
        var issues = new List<ValidationIssue>();

        ValidateHttp(profile.Http, issues);
        ValidateRelay(profile.Relay, issues);
        ValidateMatch(profile.Match, issues);
        ValidatePreflight(profile.Preflight, issues);
        ValidateUiAutomation(profile.UiAutomation, issues);
        ValidateDiagnostics(profile.Diagnostics, issues);

        return issues;
    }

    public async Task<SettingsSaveResult> SaveAsync(ApplicationSettingsProfileViewModel profile, CancellationToken cancellationToken)
    {
        var issues = Validate(profile);
        if (issues.Count > 0)
        {
            _logger.LogWarning("Settings validation failed; rejecting save request with {Count} issues.", issues.Count);
            return SettingsSaveResult.Failure(issues);
        }

        var newStorage = profile.ToStorageModel();
        var currentStorage = await ReadStorageAsync(cancellationToken).ConfigureAwait(false) ?? BuildStorageFromOptions();
        var restartSections = DetermineRestartSections(currentStorage, newStorage);

        Directory.CreateDirectory(Path.GetDirectoryName(_configPath) ?? AppContext.BaseDirectory);
        await using (var stream = new FileStream(_configPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            await JsonSerializer.SerializeAsync(stream, newStorage, _serializerOptions, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Settings saved to {Path}. Restart required for: {Sections}", _configPath, restartSections.Count == 0 ? "none" : string.Join(", ", restartSections));
        return SettingsSaveResult.Completed(restartSections);
    }

    private async Task<ApplicationSettingsStorage?> ReadStorageAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_configPath))
        {
            return null;
        }

        await using var stream = new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<ApplicationSettingsStorage>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private ApplicationSettingsStorage BuildStorageFromOptions()
    {
        return new ApplicationSettingsProfileViewModel
        {
            Http = HttpSettingsViewModel.FromOptions(_httpOptions.CurrentValue),
            Relay = RelaySettingsViewModel.FromOptions(_relayOptions.CurrentValue),
            Match = MatchSettingsViewModel.FromOptions(_matchOptions.CurrentValue),
            Preflight = PreflightSettingsViewModel.FromOptions(_preflightOptions.CurrentValue),
            UiAutomation = UiAutomationSettingsViewModel.FromOptions(_uiAutomationOptions.CurrentValue),
            Diagnostics = DiagnosticsSettingsViewModel.FromOptions(_diagnosticsOptions.CurrentValue)
        }.ToStorageModel();
    }

    private static List<string> DetermineRestartSections(ApplicationSettingsStorage? current, ApplicationSettingsStorage updated)
    {
        var sections = new List<string>();
        if (!AreHttpOptionsEqual(current?.Http, updated.Http))
        {
            sections.Add("Http");
        }

        return sections;
    }

    private static bool AreHttpOptionsEqual(HttpOptions? left, HttpOptions? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return SequenceEqual(left.Urls, right.Urls)
               && string.Equals(left.BearerToken ?? string.Empty, right.BearerToken ?? string.Empty, StringComparison.Ordinal)
               && SequenceEqual(left.AllowedCidrs, right.AllowedCidrs)
               && left.RequestTimeoutSeconds == right.RequestTimeoutSeconds;
    }

    private static bool SequenceEqual(IEnumerable<string>? left, IEnumerable<string>? right)
    {
        var first = left?.Select(v => v?.Trim()).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray() ?? Array.Empty<string>();
        var second = right?.Select(v => v?.Trim()).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray() ?? Array.Empty<string>();
        if (first.Length != second.Length)
        {
            return false;
        }

        for (var i = 0; i < first.Length; i++)
        {
            if (!string.Equals(first[i], second[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateHttp(HttpSettingsViewModel http, ICollection<ValidationIssue> issues)
    {
        if (http.Urls.Count == 0)
        {
            issues.Add(new ValidationIssue("Http.Urls", "At least one URL is required."));
        }

        foreach (var url in http.Urls)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var parsed) || string.IsNullOrWhiteSpace(parsed.Host))
            {
                issues.Add(new ValidationIssue("Http.Urls", $"Invalid URL '{url}'."));
            }
        }

        foreach (var cidr in http.AllowedCidrs)
        {
            if (!TryParseCidr(cidr))
            {
                issues.Add(new ValidationIssue("Http.AllowedCidrs", $"CIDR '{cidr}' is invalid."));
            }
        }

        if (http.RequestTimeoutSeconds <= 0)
        {
            issues.Add(new ValidationIssue("Http.RequestTimeoutSeconds", "Request timeout must be greater than zero."));
        }
    }

    private static void ValidateRelay(RelaySettingsViewModel relay, ICollection<ValidationIssue> issues)
    {
        if (relay.Enabled)
        {
            if (string.IsNullOrWhiteSpace(relay.Url))
            {
                issues.Add(new ValidationIssue("Relay.Url", "Relay URL is required when relay is enabled."));
            }
            else if (!Uri.TryCreate(relay.Url, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            {
                issues.Add(new ValidationIssue("Relay.Url", "Relay URL must be an absolute URI."));
            }
        }
    }

    private static void ValidateMatch(MatchSettingsViewModel match, ICollection<ValidationIssue> issues)
    {
        if (match.LtDisplayedDurationSec <= 0)
        {
            issues.Add(new ValidationIssue("Match.LtDisplayedDurationSec", "Displayed duration must be greater than zero."));
        }

        if (match.AutoEndNoPlantAtSec <= 0 || match.AutoEndNoPlantAtSec > match.LtDisplayedDurationSec)
        {
            issues.Add(new ValidationIssue("Match.AutoEndNoPlantAtSec", "Auto-end threshold must be greater than zero and less than or equal to the displayed duration."));
        }

        if (match.DefuseWindowSec <= 0)
        {
            issues.Add(new ValidationIssue("Match.DefuseWindowSec", "Defuse window must be greater than zero."));
        }

        if (match.ClockExpectedHz <= 0)
        {
            issues.Add(new ValidationIssue("Match.ClockExpectedHz", "Clock frequency must be greater than zero."));
        }

        if (match.LatencyWindow <= 0)
        {
            issues.Add(new ValidationIssue("Match.LatencyWindow", "Latency window must be greater than zero."));
        }

        if (match.PropSessionTimeoutSeconds <= 0)
        {
            issues.Add(new ValidationIssue("Match.PropSessionTimeoutSeconds", "Prop timeout must be greater than zero."));
        }

        if (match.FinalDataTimeoutMs <= 0)
        {
            issues.Add(new ValidationIssue("Match.FinalDataTimeoutMs", "Final data timeout must be greater than zero."));
        }
    }

    private static void ValidatePreflight(PreflightSettingsViewModel preflight, ICollection<ValidationIssue> issues)
    {
        if (preflight.ExpectedTeamNames.Count != 2)
        {
            issues.Add(new ValidationIssue("Preflight.ExpectedTeamNames", "Exactly two team names are required."));
        }

        if (string.IsNullOrWhiteSpace(preflight.ExpectedPlayerNamePattern))
        {
            issues.Add(new ValidationIssue("Preflight.ExpectedPlayerNamePattern", "Player name pattern is required."));
        }
        else
        {
            try
            {
                _ = new Regex(preflight.ExpectedPlayerNamePattern);
            }
            catch (Exception)
            {
                issues.Add(new ValidationIssue("Preflight.ExpectedPlayerNamePattern", "Player name pattern must be a valid regular expression."));
            }
        }
    }

    private static void ValidateUiAutomation(UiAutomationSettingsViewModel automation, ICollection<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(automation.ProcessName))
        {
            issues.Add(new ValidationIssue("UiAutomation.ProcessName", "Process name is required."));
        }

        if (string.IsNullOrWhiteSpace(automation.WindowTitleRegex))
        {
            issues.Add(new ValidationIssue("UiAutomation.WindowTitleRegex", "Window title regex is required."));
        }
        else
        {
            try
            {
                _ = new Regex(automation.WindowTitleRegex);
            }
            catch (Exception)
            {
                issues.Add(new ValidationIssue("UiAutomation.WindowTitleRegex", "Window title regex must be valid."));
            }
        }

        if (automation.FocusTimeoutMs <= 0)
        {
            issues.Add(new ValidationIssue("UiAutomation.FocusTimeoutMs", "Focus timeout must be greater than zero."));
        }

        if (automation.PostShortcutDelayMs <= 0)
        {
            issues.Add(new ValidationIssue("UiAutomation.PostShortcutDelayMs", "Shortcut delay must be greater than zero."));
        }

        if (automation.DebounceWindowMs <= 0)
        {
            issues.Add(new ValidationIssue("UiAutomation.DebounceWindowMs", "Debounce window must be greater than zero."));
        }
    }

    private static void ValidateDiagnostics(DiagnosticsSettingsViewModel diagnostics, ICollection<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(diagnostics.LogLevel))
        {
            issues.Add(new ValidationIssue("Diagnostics.LogLevel", "Log level is required."));
        }

        if (diagnostics.WriteToFile && string.IsNullOrWhiteSpace(diagnostics.LogPath))
        {
            issues.Add(new ValidationIssue("Diagnostics.LogPath", "Log path is required when file logging is enabled."));
        }
    }

    private static bool TryParseCidr(string cidr)
    {
        var parts = cidr.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!System.Net.IPAddress.TryParse(parts[0], out var network))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var prefix))
        {
            return false;
        }

        if (network.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        return prefix is >= 0 and <= 32;
    }
}

public sealed record ValidationIssue(string Field, string Message);

public sealed record SettingsSaveResult(bool Success, IReadOnlyList<string> RestartRequiredSections, IReadOnlyList<ValidationIssue> Errors)
{
    public static SettingsSaveResult Failure(IReadOnlyList<ValidationIssue> errors) => new(false, Array.Empty<string>(), errors);

    public static SettingsSaveResult Completed(IReadOnlyList<string> restartRequiredSections) => new(true, restartRequiredSections, Array.Empty<ValidationIssue>());
}
