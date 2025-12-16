using System;
using System.Drawing;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LaserTag.Defusal.Domain;
using LaserTag.Defusal.Interop;
using LaserTag.Defusal.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaserTag.Defusal.Ui;

/// <summary>
/// Displays the latest combined relay payload in a stable JSON layout.
/// </summary>
public sealed class RelayMonitorForm : Form
{
    private readonly RelaySnapshotCache _cache;
    private readonly ILogger<RelayMonitorForm> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly IOptionsMonitor<RelayOptions> _relayOptions;
    private readonly IDisposable? _optionsReload;

    private readonly Label _relayStatusLabel = new() { AutoSize = true };
    private readonly Label _lastUpdatedLabel = new() { AutoSize = true };
    private readonly Label _staleLabel = new() { AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    private readonly TextBox _matchJson = CreateJsonViewer();
    private readonly TextBox _propJson = CreateJsonViewer();
    private readonly TextBox _combinedJson = CreateJsonViewer();

    public RelayMonitorForm(
        RelaySnapshotCache cache,
        IOptionsMonitor<RelayOptions> relayOptions,
        ILogger<RelayMonitorForm> logger)
    {
        _cache = cache;
        _logger = logger;
        _relayOptions = relayOptions;
        _optionsReload = _relayOptions.OnChange(OnRelayOptionsChanged);

        Text = "Relay Monitor";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(800, 600);
        MinimumSize = new Size(720, 520);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12)
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var headerPanel = BuildHeaderPanel();
        layout.Controls.Add(headerPanel, 0, 0);
        layout.SetColumnSpan(headerPanel, 2);

        var leftPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };

        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        leftPanel.Controls.Add(BuildGroupBox("Match Payload", _matchJson), 0, 0);
        leftPanel.Controls.Add(BuildGroupBox("Prop Payload", _propJson), 0, 1);

        layout.Controls.Add(leftPanel, 0, 1);
        layout.Controls.Add(BuildGroupBox("Combined Payload", _combinedJson), 1, 1);

        Controls.Add(layout);

        Shown += (_, _) => RenderSnapshot(_cache.GetSnapshot());
        FormClosed += OnFormClosed;
        _cache.SnapshotChanged += OnSnapshotChanged;

        UpdateRelayStatusLabel(_relayOptions.CurrentValue);
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

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        _lastUpdatedLabel.Text = "Last updated: awaiting payload";
        _staleLabel.Text = "STALE";
        _staleLabel.ForeColor = Color.DarkRed;

        panel.Controls.Add(_relayStatusLabel, 0, 0);
        panel.SetColumnSpan(_relayStatusLabel, 2);
        panel.Controls.Add(_lastUpdatedLabel, 0, 1);
        panel.Controls.Add(_staleLabel, 1, 1);

        UpdateRelayStatusLabel(_relayOptions.CurrentValue);

        return panel;
    }

    private static GroupBox BuildGroupBox(string title, Control inner)
    {
        var box = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        inner.Dock = DockStyle.Fill;
        box.Controls.Add(inner);
        return box;
    }

    private void OnSnapshotChanged(object? sender, RelaySnapshotEventArgs e)
    {
        if (!IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(() => RenderSnapshot(e.Snapshot)));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to render relay snapshot.");
        }
    }

    private void RenderSnapshot(RelaySnapshotState snapshot)
    {
        if (snapshot.LastUpdatedUtc is { } timestamp)
        {
            _lastUpdatedLabel.Text = $"Last updated (UTC): {timestamp:u}";
        }
        else
        {
            _lastUpdatedLabel.Text = "Last updated: awaiting payload";
        }

        if (snapshot.IsStale)
        {
            _staleLabel.Text = "STALE (>5s)";
            _staleLabel.ForeColor = Color.DarkRed;
        }
        else
        {
            _staleLabel.Text = "Fresh";
            _staleLabel.ForeColor = Color.DarkGreen;
        }

        if (snapshot.Payload is null)
        {
            const string message = "No payload has been relayed yet.";
            UpdateJsonViewer(_matchJson, message);
            UpdateJsonViewer(_propJson, message);
            UpdateJsonViewer(_combinedJson, message);
            return;
        }

        var matchJson = JsonSerializer.Serialize(snapshot.Payload.Match, _jsonOptions);
        var propJson = JsonSerializer.Serialize(snapshot.Payload.Prop, _jsonOptions);
        var combinedJson = JsonSerializer.Serialize(snapshot.Payload, _jsonOptions);

        UpdateJsonViewer(_matchJson, matchJson);
        UpdateJsonViewer(_propJson, propJson);
        UpdateJsonViewer(_combinedJson, combinedJson);
    }

    private static void UpdateJsonViewer(TextBox textBox, string newText)
    {
        if (textBox.TextLength == newText.Length && textBox.Text == newText)
        {
            return;
        }

        if (!textBox.IsHandleCreated)
        {
            textBox.Text = newText;
            return;
        }

        var scrollInfo = new NativeMethods.SCROLLINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.SCROLLINFO>(),
            fMask = NativeMethods.SIF_ALL
        };

        NativeMethods.GetScrollInfo(textBox.Handle, NativeMethods.SB_VERT, ref scrollInfo);

        var previousPos = scrollInfo.nPos;
        var wasAtBottom = scrollInfo.nMax <= 0 || previousPos >= scrollInfo.nMax - scrollInfo.nPage;

        textBox.SuspendLayout();
        try
        {
            NativeMethods.SendMessage(textBox.Handle, NativeMethods.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);

            textBox.Text = newText;

            if (textBox.TextLength == 0)
            {
                return;
            }

            var updatedInfo = new NativeMethods.SCROLLINFO
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.SCROLLINFO>(),
                fMask = NativeMethods.SIF_ALL
            };

            NativeMethods.GetScrollInfo(textBox.Handle, NativeMethods.SB_VERT, ref updatedInfo);

            if (wasAtBottom)
            {
                textBox.SelectionStart = textBox.TextLength;
                textBox.SelectionLength = 0;
                textBox.ScrollToCaret();
                return;
            }

            var targetPos = Math.Max(0, Math.Min(previousPos, updatedInfo.nMax - (int)updatedInfo.nPage + 1));

            updatedInfo.nPos = targetPos;
            updatedInfo.fMask = NativeMethods.SIF_POS;

            NativeMethods.SetScrollInfo(textBox.Handle, NativeMethods.SB_VERT, ref updatedInfo, true);
            NativeMethods.SendMessage(
                textBox.Handle,
                NativeMethods.WM_VSCROLL,
                new IntPtr(NativeMethods.MakeWParam(NativeMethods.SB_THUMBPOSITION, targetPos)),
                IntPtr.Zero);
        }
        finally
        {
            NativeMethods.SendMessage(textBox.Handle, NativeMethods.WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
            textBox.Invalidate();
            textBox.ResumeLayout();
        }
    }

    private void OnRelayOptionsChanged(RelayOptions options)
    {
        if (!IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(() => UpdateRelayStatusLabel(options)));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to update relay monitor header after options change.");
        }
    }

    private void UpdateRelayStatusLabel(RelayOptions options)
    {
        _relayStatusLabel.Text = options.Enabled
            ? "Relay Enabled - monitoring downstream payloads."
            : "Relay Disabled - monitor displays most recent cached payload only.";
        _relayStatusLabel.ForeColor = options.Enabled ? Color.DarkGreen : Color.DarkRed;
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs e)
    {
        _cache.SnapshotChanged -= OnSnapshotChanged;
        _optionsReload?.Dispose();
    }

    private static TextBox CreateJsonViewer()
    {
        return new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            ReadOnly = true,
            Font = new Font("Consolas", 9f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            WordWrap = false
        };
    }
}
