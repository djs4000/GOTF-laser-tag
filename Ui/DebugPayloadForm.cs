using System;
using System.Drawing;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LaserTag.Defusal.Domain;
using LaserTag.Defusal.Services;
using Microsoft.Extensions.Logging;

namespace LaserTag.Defusal.Ui;

/// <summary>
/// Provides a UI for crafting and sending debug payloads through the relay pipeline.
/// </summary>
public sealed class DebugPayloadForm : Form
{
    private readonly DebugPayloadService _debugPayloadService;
    private readonly ILogger<DebugPayloadForm> _logger;
    private readonly ComboBox _payloadTypeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _jsonEditor = CreateJsonEditor();
    private readonly Button _formatButton = new() { Text = "&Format JSON", AutoSize = true };
    private readonly Button _sendButton = new() { Text = "&Send", AutoSize = true };
    private readonly Button _closeButton = new() { Text = "Close", AutoSize = true };
    private readonly Label _statusLabel = new() { AutoSize = true };
    private readonly DebugPayloadTemplate _template = new();
    private static readonly JsonSerializerOptions JsonPrettyPrintOptions = new() { WriteIndented = true };

    public DebugPayloadForm(DebugPayloadService debugPayloadService, ILogger<DebugPayloadForm> logger)
    {
        _debugPayloadService = debugPayloadService;
        _logger = logger;

        Text = "Debug Payload Injector";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 700);
        MinimumSize = new Size(950, 600);

        BuildLayout();

        _payloadTypeCombo.Items.AddRange(Enum.GetNames(typeof(DebugPayloadType)));
        _payloadTypeCombo.SelectedIndex = 0;
        _jsonEditor.Text = SampleCombinedPayload();

        _formatButton.Click += (_, _) => FormatJsonContent();
        _sendButton.Click += async (_, _) => await OnSendAsync().ConfigureAwait(false);
        _closeButton.Click += (_, _) => Close();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
            AutoSize = false
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildHeaderPanel(), 0, 0);
        root.Controls.Add(BuildEditorPanel(), 0, 1);
        root.Controls.Add(BuildFooterPanel(), 0, 2);

        Controls.Add(root);
    }

    private Control BuildHeaderPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 2,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        panel.Controls.Add(new Label
        {
            Text = "Payload type:",
            Anchor = AnchorStyles.Left,
            AutoSize = true
        }, 0, 0);

        panel.Controls.Add(_payloadTypeCombo, 1, 0);

        var helper = new Label
        {
            Text = "Edit the JSON payload below. Use Match or Prop types to merge with the latest opposite payload, or Combined to send a full payload.",
            AutoSize = true,
            MaximumSize = new Size(780, 0)
        };
        panel.Controls.Add(helper, 0, 1);
        panel.SetColumnSpan(helper, 2);

        return panel;
    }

    private Control BuildEditorPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 8)
        };
        panel.Controls.Add(_jsonEditor);
        return panel;
    }

    private Control BuildFooterPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 8, 0, 0)
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _statusLabel.Text = "Ready.";
        _statusLabel.AutoSize = true;
        _statusLabel.Margin = new Padding(0, 0, 12, 0);
        _statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        _formatButton.Margin = new Padding(0, 0, 6, 0);
        _sendButton.Margin = new Padding(0, 0, 6, 0);
        _closeButton.Margin = new Padding(0);

        buttonPanel.Controls.Add(_formatButton);
        buttonPanel.Controls.Add(_sendButton);
        buttonPanel.Controls.Add(_closeButton);

        panel.Controls.Add(_statusLabel, 0, 0);
        panel.Controls.Add(buttonPanel, 1, 0);
        return panel;
    }

    private async Task OnSendAsync()
    {
        if (_payloadTypeCombo.SelectedItem is null)
        {
            MessageBox.Show(this, "Select a payload type before sending.", "Debug Payloads", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var payloadType = Enum.Parse<DebugPayloadType>(_payloadTypeCombo.SelectedItem.ToString()!);
        var jsonBody = _jsonEditor.Text;

        try
        {
            ToggleInput(false);
            _statusLabel.Text = "Sending payload...";

            var result = await _debugPayloadService.SendAsync(payloadType, jsonBody, CancellationToken.None).ConfigureAwait(true);
            _template.PayloadType = payloadType;
            _template.JsonBody = jsonBody;
            _template.LastSentUtc = DateTimeOffset.UtcNow;
            _template.LastSendSucceeded = result.Success;
            _template.LastStatusCode = result.StatusCode;
            _template.LastMessage = result.Message;

            _statusLabel.Text = result.Message;
            _statusLabel.ForeColor = result.Success ? Color.DarkGreen : Color.DarkRed;
        }
        catch (DebugPayloadValidationException ex)
        {
            _logger.LogWarning(ex, "Debug payload validation failed.");
            MessageBox.Show(this, ex.Message, "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _statusLabel.Text = ex.Message;
            _statusLabel.ForeColor = Color.DarkRed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Debug payload send failed.");
            MessageBox.Show(this, "An unexpected error occurred while sending the payload. Check logs for details.", "Debug Payloads", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "Unexpected error while sending payload.";
            _statusLabel.ForeColor = Color.DarkRed;
        }
        finally
        {
            ToggleInput(true);
        }
    }

    private void ToggleInput(bool enabled)
    {
        _payloadTypeCombo.Enabled = enabled;
        _jsonEditor.ReadOnly = !enabled;
        _sendButton.Enabled = enabled;
        _formatButton.Enabled = enabled;
    }

    private static TextBox CreateJsonEditor()
    {
        return new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 10f),
            BackColor = Color.Black,
            ForeColor = Color.LightGreen,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
    }

    private static string SampleCombinedPayload()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $$"""
        {
          "timestamp": {{timestamp}},
          "match": {
            "id": "debug-match",
            "status": "Running",
            "remaining_time_ms": 60000,
            "winner_team": null,
            "players": []
          },
          "prop": {
            "timestamp": {{timestamp}},
            "state": "armed",
            "timer_ms": 30000,
            "uptime_ms": 1000
          }
        }
        """;
    }

    private void FormatJsonContent()
    {
        var json = _jsonEditor.Text;
        if (string.IsNullOrWhiteSpace(json))
        {
            _statusLabel.Text = "JSON editor is empty.";
            _statusLabel.ForeColor = Color.DarkRed;
            return;
        }

        try
        {
            var selectionStart = _jsonEditor.SelectionStart;
            using var document = JsonDocument.Parse(json);
            var formatted = JsonSerializer.Serialize(document.RootElement, JsonPrettyPrintOptions);
            _jsonEditor.Text = formatted;
            _jsonEditor.SelectionStart = Math.Min(selectionStart, _jsonEditor.TextLength);
            _jsonEditor.ScrollToCaret();

            _statusLabel.Text = "JSON formatted.";
            _statusLabel.ForeColor = Color.DarkGreen;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Unable to format JSON input.");
            MessageBox.Show(this, "The JSON content could not be parsed. Fix errors before formatting.", "JSON Formatting", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _statusLabel.Text = "JSON formatting failed.";
            _statusLabel.ForeColor = Color.DarkRed;
        }
    }
}
