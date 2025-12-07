using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LaserTag.Defusal.Domain;
using LaserTag.Defusal.Services;
using Microsoft.Extensions.Logging;

namespace LaserTag.Defusal.Ui;

/// <summary>
/// WinForms editor that surfaces every appsettings.json section with inline validation and restart prompts.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly SettingsPersistenceService _persistenceService;
    private readonly ILogger<SettingsForm> _logger;
    private readonly ErrorProvider _errorProvider = new();
    private readonly ToolTip _toolTip = new();
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Button _saveButton = new() { Text = "&Save", AutoSize = true };
    private readonly Button _cancelButton = new() { Text = "Close", AutoSize = true };
    private readonly Button _resetSectionButton = new() { Text = "Reset Section", AutoSize = true };

    private readonly TextBox _httpUrlsText = CreateMultiline();
    private readonly TextBox _httpBearerText = new();
    private readonly TextBox _httpCidrsText = CreateMultiline();
    private readonly NumericUpDown _httpTimeoutNumeric = CreateNumeric(1, 120, 5);

    private readonly CheckBox _relayEnabledCheck = new() { Text = "Relay Enabled" };
    private readonly TextBox _relayUrlText = new();
    private readonly TextBox _relayBearerText = new();
    private readonly CheckBox _relaySchemaCheck = new() { Text = "Validate payload schema before send" };

    private readonly NumericUpDown _matchDurationNumeric = CreateNumeric(30, 1200, 219);
    private readonly NumericUpDown _matchAutoEndNumeric = CreateNumeric(30, 1200, 180);
    private readonly NumericUpDown _matchDefuseNumeric = CreateNumeric(5, 120, 40);
    private readonly NumericUpDown _matchClockHzNumeric = CreateNumeric(1, 60, 10);
    private readonly NumericUpDown _matchLatencyWindowNumeric = CreateNumeric(1, 120, 10);
    private readonly NumericUpDown _matchPreflightLengthNumeric = CreateNumeric(30, 1200, 219);
    private readonly NumericUpDown _matchPropTimeoutNumeric = CreateNumeric(1, 120, 10);
    private readonly NumericUpDown _matchFinalDataTimeoutNumeric = CreateNumeric(100, 10000, 2000);

    private readonly CheckBox _preflightEnabledCheck = new() { Text = "Enable preflight validation" };
    private readonly TextBox _preflightTeamsText = CreateMultiline();
    private readonly TextBox _preflightPatternText = new();
    private readonly CheckBox _preflightCancelCheck = new() { Text = "Cancel match automatically on failures" };

    private readonly TextBox _uiProcessText = new();
    private readonly TextBox _uiWindowRegexText = new();
    private readonly NumericUpDown _uiFocusTimeoutNumeric = CreateNumeric(100, 5000, 1500);
    private readonly NumericUpDown _uiShortcutDelayNumeric = CreateNumeric(50, 2000, 150);
    private readonly NumericUpDown _uiDebounceNumeric = CreateNumeric(100, 5000, 2000);

    private readonly ComboBox _diagnosticsLevelCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _diagnosticsWriteFileCheck = new() { Text = "Write logs to file" };
    private readonly TextBox _diagnosticsPathText = new();

    private ApplicationSettingsProfileViewModel? _profile;
    private ApplicationSettingsProfileViewModel? _persistedProfile;
    private bool _isLoading;
    private readonly Dictionary<string, Control> _errorTargets;

    public SettingsForm(SettingsPersistenceService persistenceService, ILogger<SettingsForm> logger)
    {
        _persistenceService = persistenceService;
        _logger = logger;
        _errorTargets = BuildErrorTargetMap();

        Text = "Application Settings";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 700);
        MinimumSize = new Size(800, 600);

        _errorProvider.BlinkStyle = ErrorBlinkStyle.NeverBlink;

        BuildTabs();
        BuildFooter();

        Controls.Add(_tabs);

        Load += async (_, _) => await ReloadProfileAsync().ConfigureAwait(false);
        _saveButton.Click += async (_, _) => await OnSaveAsync().ConfigureAwait(false);
        _cancelButton.Click += (_, _) => Close();
        _resetSectionButton.Click += (_, _) => ResetActiveSection();

        _diagnosticsLevelCombo.Items.AddRange(new object[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None" });
    }

    private void BuildTabs()
    {
        _tabs.TabPages.Add(CreateTab("Http", BuildHttpPanel()));
        _tabs.TabPages.Add(CreateTab("Relay", BuildRelayPanel()));
        _tabs.TabPages.Add(CreateTab("Match", BuildMatchPanel()));
        _tabs.TabPages.Add(CreateTab("Preflight", BuildPreflightPanel()));
        _tabs.TabPages.Add(CreateTab("UiAutomation", BuildUiAutomationPanel()));
        _tabs.TabPages.Add(CreateTab("Diagnostics", BuildDiagnosticsPanel()));
    }

    private void BuildFooter()
    {
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(12)
        };

        footer.Controls.Add(_cancelButton);
        footer.Controls.Add(_saveButton);
        footer.Controls.Add(_resetSectionButton);

        Controls.Add(footer);
    }

    private TabPage CreateTab(string title, Control content)
    {
        var page = new TabPage(title) { Padding = new Padding(12) };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    private Control BuildHttpPanel()
    {
        var panel = CreateSectionTable();
        AddRow(panel, "Server URLs", _httpUrlsText, "One URL per line (e.g., http://127.0.0.1:5055).");
        AddRow(panel, "Bearer token", _httpBearerText, "Optional HTTP bearer token required for inbound POSTs.");
        AddRow(panel, "Allowed CIDRs", _httpCidrsText, "CIDR ranges allowed to post inbound data.");
        AddRow(panel, "Request timeout (s)", _httpTimeoutNumeric, "Duration before inbound HTTP requests are considered failed.");
        return panel;
    }

    private Control BuildRelayPanel()
    {
        var panel = CreateSectionTable();
        AddRow(panel, "Relay Enabled", _relayEnabledCheck, "Toggle downstream relay transmissions.");
        AddRow(panel, "Relay URL", _relayUrlText, "Absolute URL for the downstream relay endpoint.");
        AddRow(panel, "Relay bearer token", _relayBearerText, "Optional bearer token attached to outbound relay requests.");
        AddRow(panel, "Schema validation", _relaySchemaCheck, "Validate payload JSON against the combined schema before sending.");
        return panel;
    }

    private Control BuildMatchPanel()
    {
        var panel = CreateSectionTable();
        AddRow(panel, "Displayed duration (s)", _matchDurationNumeric, "Match duration shown to operators.");
        AddRow(panel, "Auto-end (s)", _matchAutoEndNumeric, "Stop the match if no plant occurs before this point.");
        AddRow(panel, "Defuse window (s)", _matchDefuseNumeric, "Defuse window granted when bomb planted ≥ 180s.");
        AddRow(panel, "Clock expected Hz", _matchClockHzNumeric, "Clock cadence expected from the LT host.");
        AddRow(panel, "Latency window", _matchLatencyWindowNumeric, "Number of samples used to estimate latency.");
        AddRow(panel, "Preflight match length (s)", _matchPreflightLengthNumeric, "Reference duration to display in preflight guidance.");
        AddRow(panel, "Prop timeout (s)", _matchPropTimeoutNumeric, "Prop heartbeat expiration threshold.");
        AddRow(panel, "Final data timeout (ms)", _matchFinalDataTimeoutNumeric, "Wait time for final host packet before forcing relay.");
        return panel;
    }

    private Control BuildPreflightPanel()
    {
        var panel = CreateSectionTable();
        AddRow(panel, "Preflight enabled", _preflightEnabledCheck, "Toggle preflight checks entirely.");
        AddRow(panel, "Expected team names", _preflightTeamsText, "Two lines describing attacker/defender names.");
        AddRow(panel, "Player pattern", _preflightPatternText, "Regex used to validate player IDs.");
        AddRow(panel, "Cancel on failure", _preflightCancelCheck, "Automatically cancel the match when validations fail.");
        return panel;
    }

    private Control BuildUiAutomationPanel()
    {
        var panel = CreateSectionTable();
        AddRow(panel, "Process name", _uiProcessText, "Win32 process hosting ICE.");
        AddRow(panel, "Window title regex", _uiWindowRegexText, "Regex used to locate the ICE window.");
        AddRow(panel, "Focus timeout (ms)", _uiFocusTimeoutNumeric, "Milliseconds to wait when focusing ICE.");
        AddRow(panel, "Shortcut delay (ms)", _uiShortcutDelayNumeric, "Post-focus delay before sending Ctrl+S.");
        AddRow(panel, "Debounce window (ms)", _uiDebounceNumeric, "Minimum interval between focus attempts.");
        return panel;
    }

    private Control BuildDiagnosticsPanel()
    {
        var panel = CreateSectionTable();
        AddRow(panel, "Log level", _diagnosticsLevelCombo, "Minimum log level emitted.");
        AddRow(panel, "Write to file", _diagnosticsWriteFileCheck, "Persist logs to disk in addition to console.");
        AddRow(panel, "Log path", _diagnosticsPathText, "Rolling file path when file logging is enabled.");
        return panel;
    }

    private static TableLayoutPanel CreateSectionTable()
    {
        return new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
    }

    private void AddRow(TableLayoutPanel panel, string label, Control control, string helpText)
    {
        var rowIndex = panel.RowCount;
        panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new Label
        {
            Text = label,
            AutoSize = true,
            Margin = new Padding(0, 4, 12, 4)
        };

        control.Margin = new Padding(0, 4, 0, 4);
        panel.Controls.Add(header, 0, rowIndex);
        panel.Controls.Add(control, 1, rowIndex);
        if (!string.IsNullOrWhiteSpace(helpText))
        {
            _toolTip.SetToolTip(header, helpText);
            _toolTip.SetToolTip(control, helpText);
        }
    }

    private async Task ReloadProfileAsync()
    {
        try
        {
            UseWaitCursor = true;
            _isLoading = true;
            var profile = await _persistenceService.LoadAsync(CancellationToken.None).ConfigureAwait(true);
            _profile = profile;
            _persistedProfile = profile.Clone();
            ApplyProfileToControls(profile);
            ClearErrors();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings profile.");
            MessageBox.Show(this, "Unable to load settings. Check logs for details.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _isLoading = false;
            UseWaitCursor = false;
        }
    }

    private async Task OnSaveAsync()
    {
        if (_profile is null)
        {
            return;
        }

        UpdateProfileFromControls();
        var issues = _persistenceService.Validate(_profile);
        if (ApplyValidation(issues))
        {
            return;
        }

        try
        {
            UseWaitCursor = true;
            var result = await _persistenceService.SaveAsync(_profile, CancellationToken.None).ConfigureAwait(true);
            if (!result.Success)
            {
                ApplyValidation(result.Errors);
                return;
            }

            _persistedProfile = _profile.Clone();
            var restartMessage = result.RestartRequiredSections.Count > 0
                ? $"Restart required for: {string.Join(", ", result.RestartRequiredSections)}."
                : "Changes applied immediately.";
            MessageBox.Show(this, $"Settings saved successfully. {restartMessage}", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ClearErrors();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist settings.");
            MessageBox.Show(this, "Saving settings failed. Check logs for details.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void ApplyProfileToControls(ApplicationSettingsProfileViewModel profile)
    {
        _httpUrlsText.Text = string.Join(Environment.NewLine, profile.Http.Urls);
        _httpBearerText.Text = profile.Http.BearerToken ?? string.Empty;
        _httpCidrsText.Text = string.Join(Environment.NewLine, profile.Http.AllowedCidrs);
        _httpTimeoutNumeric.Value = profile.Http.RequestTimeoutSeconds;

        _relayEnabledCheck.Checked = profile.Relay.Enabled;
        _relayUrlText.Text = profile.Relay.Url ?? string.Empty;
        _relayBearerText.Text = profile.Relay.BearerToken ?? string.Empty;
        _relaySchemaCheck.Checked = profile.Relay.EnableSchemaValidation;

        _matchDurationNumeric.Value = profile.Match.LtDisplayedDurationSec;
        _matchAutoEndNumeric.Value = profile.Match.AutoEndNoPlantAtSec;
        _matchDefuseNumeric.Value = profile.Match.DefuseWindowSec;
        _matchClockHzNumeric.Value = profile.Match.ClockExpectedHz;
        _matchLatencyWindowNumeric.Value = profile.Match.LatencyWindow;
        _matchPreflightLengthNumeric.Value = profile.Match.PreflightExpectedMatchLengthSec;
        _matchPropTimeoutNumeric.Value = profile.Match.PropSessionTimeoutSeconds;
        _matchFinalDataTimeoutNumeric.Value = profile.Match.FinalDataTimeoutMs;

        _preflightEnabledCheck.Checked = profile.Preflight.Enabled;
        _preflightTeamsText.Text = string.Join(Environment.NewLine, profile.Preflight.ExpectedTeamNames);
        _preflightPatternText.Text = profile.Preflight.ExpectedPlayerNamePattern ?? string.Empty;
        _preflightCancelCheck.Checked = profile.Preflight.EnforceMatchCancellation;

        _uiProcessText.Text = profile.UiAutomation.ProcessName;
        _uiWindowRegexText.Text = profile.UiAutomation.WindowTitleRegex;
        _uiFocusTimeoutNumeric.Value = profile.UiAutomation.FocusTimeoutMs;
        _uiShortcutDelayNumeric.Value = profile.UiAutomation.PostShortcutDelayMs;
        _uiDebounceNumeric.Value = profile.UiAutomation.DebounceWindowMs;

        _diagnosticsLevelCombo.SelectedItem = profile.Diagnostics.LogLevel;
        if (_diagnosticsLevelCombo.SelectedIndex < 0)
        {
            _diagnosticsLevelCombo.SelectedIndex = 2; // Information
        }
        _diagnosticsWriteFileCheck.Checked = profile.Diagnostics.WriteToFile;
        _diagnosticsPathText.Text = profile.Diagnostics.LogPath ?? string.Empty;
    }

    private void UpdateProfileFromControls()
    {
        if (_profile is null || _isLoading)
        {
            return;
        }

        _profile.Http.Urls = GetLines(_httpUrlsText);
        _profile.Http.BearerToken = string.IsNullOrWhiteSpace(_httpBearerText.Text) ? null : _httpBearerText.Text.Trim();
        _profile.Http.AllowedCidrs = GetLines(_httpCidrsText);
        _profile.Http.RequestTimeoutSeconds = (int)_httpTimeoutNumeric.Value;

        _profile.Relay.Enabled = _relayEnabledCheck.Checked;
        _profile.Relay.Url = string.IsNullOrWhiteSpace(_relayUrlText.Text) ? null : _relayUrlText.Text.Trim();
        _profile.Relay.BearerToken = string.IsNullOrWhiteSpace(_relayBearerText.Text) ? null : _relayBearerText.Text.Trim();
        _profile.Relay.EnableSchemaValidation = _relaySchemaCheck.Checked;

        _profile.Match.LtDisplayedDurationSec = (int)_matchDurationNumeric.Value;
        _profile.Match.AutoEndNoPlantAtSec = (int)_matchAutoEndNumeric.Value;
        _profile.Match.DefuseWindowSec = (int)_matchDefuseNumeric.Value;
        _profile.Match.ClockExpectedHz = (int)_matchClockHzNumeric.Value;
        _profile.Match.LatencyWindow = (int)_matchLatencyWindowNumeric.Value;
        _profile.Match.PreflightExpectedMatchLengthSec = (int)_matchPreflightLengthNumeric.Value;
        _profile.Match.PropSessionTimeoutSeconds = (int)_matchPropTimeoutNumeric.Value;
        _profile.Match.FinalDataTimeoutMs = (int)_matchFinalDataTimeoutNumeric.Value;

        _profile.Preflight.Enabled = _preflightEnabledCheck.Checked;
        _profile.Preflight.ExpectedTeamNames = GetLines(_preflightTeamsText);
        _profile.Preflight.ExpectedPlayerNamePattern = _preflightPatternText.Text.Trim();
        _profile.Preflight.EnforceMatchCancellation = _preflightCancelCheck.Checked;

        _profile.UiAutomation.ProcessName = _uiProcessText.Text.Trim();
        _profile.UiAutomation.WindowTitleRegex = _uiWindowRegexText.Text.Trim();
        _profile.UiAutomation.FocusTimeoutMs = (int)_uiFocusTimeoutNumeric.Value;
        _profile.UiAutomation.PostShortcutDelayMs = (int)_uiShortcutDelayNumeric.Value;
        _profile.UiAutomation.DebounceWindowMs = (int)_uiDebounceNumeric.Value;

        _profile.Diagnostics.LogLevel = _diagnosticsLevelCombo.SelectedItem?.ToString() ?? "Information";
        _profile.Diagnostics.WriteToFile = _diagnosticsWriteFileCheck.Checked;
        _profile.Diagnostics.LogPath = _diagnosticsPathText.Text.Trim();
    }

    private static List<string> GetLines(TextBox textBox)
    {
        return textBox
            .Text
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
    }

    private bool ApplyValidation(IReadOnlyList<ValidationIssue> issues)
    {
        ClearErrors();
        if (issues.Count == 0)
        {
            return false;
        }

        foreach (var issue in issues)
        {
            if (_errorTargets.TryGetValue(issue.Field, out var control))
            {
                _errorProvider.SetError(control, issue.Message);
            }
        }

        MessageBox.Show(this, "Some settings are invalid. Hover over highlighted fields for details.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return true;
    }

    private void ClearErrors()
    {
        foreach (var control in _errorTargets.Values)
        {
            _errorProvider.SetError(control, string.Empty);
        }
    }

    private void ResetActiveSection()
    {
        if (_persistedProfile is null || _profile is null)
        {
            return;
        }

        switch (_tabs.SelectedTab?.Text)
        {
            case "Http":
                _profile.Http = _persistedProfile.Http.Clone();
                break;
            case "Relay":
                _profile.Relay = _persistedProfile.Relay.Clone();
                break;
            case "Match":
                _profile.Match = _persistedProfile.Match.Clone();
                break;
            case "Preflight":
                _profile.Preflight = _persistedProfile.Preflight.Clone();
                break;
            case "UiAutomation":
                _profile.UiAutomation = _persistedProfile.UiAutomation.Clone();
                break;
            case "Diagnostics":
                _profile.Diagnostics = _persistedProfile.Diagnostics.Clone();
                break;
            default:
                return;
        }

        ApplyProfileToControls(_profile);
        ClearErrors();
    }

    private Dictionary<string, Control> BuildErrorTargetMap()
    {
        return new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase)
        {
            ["Http.Urls"] = _httpUrlsText,
            ["Http.BearerToken"] = _httpBearerText,
            ["Http.AllowedCidrs"] = _httpCidrsText,
            ["Http.RequestTimeoutSeconds"] = _httpTimeoutNumeric,
            ["Relay.Url"] = _relayUrlText,
            ["Match.LtDisplayedDurationSec"] = _matchDurationNumeric,
            ["Match.AutoEndNoPlantAtSec"] = _matchAutoEndNumeric,
            ["Match.DefuseWindowSec"] = _matchDefuseNumeric,
            ["Match.ClockExpectedHz"] = _matchClockHzNumeric,
            ["Match.LatencyWindow"] = _matchLatencyWindowNumeric,
            ["Match.PreflightExpectedMatchLengthSec"] = _matchPreflightLengthNumeric,
            ["Match.PropSessionTimeoutSeconds"] = _matchPropTimeoutNumeric,
            ["Match.FinalDataTimeoutMs"] = _matchFinalDataTimeoutNumeric,
            ["Preflight.ExpectedTeamNames"] = _preflightTeamsText,
            ["Preflight.ExpectedPlayerNamePattern"] = _preflightPatternText,
            ["UiAutomation.ProcessName"] = _uiProcessText,
            ["UiAutomation.WindowTitleRegex"] = _uiWindowRegexText,
            ["UiAutomation.FocusTimeoutMs"] = _uiFocusTimeoutNumeric,
            ["UiAutomation.PostShortcutDelayMs"] = _uiShortcutDelayNumeric,
            ["UiAutomation.DebounceWindowMs"] = _uiDebounceNumeric,
            ["Diagnostics.LogLevel"] = _diagnosticsLevelCombo,
            ["Diagnostics.LogPath"] = _diagnosticsPathText
        };
    }

    private static TextBox CreateMultiline()
    {
        return new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 70
        };
    }

    private static NumericUpDown CreateNumeric(int minimum, int maximum, int value)
    {
        return new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Increment = 1,
            Width = 120
        };
    }
}
