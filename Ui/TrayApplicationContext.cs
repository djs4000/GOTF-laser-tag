using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using LaserTag.Defusal.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LaserTag.Defusal.Ui;

/// <summary>
/// Custom application context that hosts the tray icon and controls the lifecycle of the status window.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly StatusForm _statusForm;
    private readonly MatchCoordinator _coordinator;
    private readonly ILogger<TrayApplicationContext> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NotifyIcon _notifyIcon;

    public TrayApplicationContext(
        StatusForm statusForm,
        MatchCoordinator coordinator,
        ILogger<TrayApplicationContext> logger,
        IServiceProvider serviceProvider,
        IServiceScopeFactory scopeFactory)
    {
        _statusForm = statusForm;
        _coordinator = coordinator;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _scopeFactory = scopeFactory;

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "ICE Defusal Monitor",
            ContextMenuStrip = BuildContextMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => ShowWindow();

        ShowWindow();
    }

    /// <summary>
    /// Provides access to the root service provider so utility forms can be resolved via DI.
    /// </summary>
    public IServiceProvider Services => _serviceProvider;

    /// <summary>
    /// Creates a new service scope for owned WinForms instances. Callers are responsible for disposing the scope.
    /// </summary>
    public IServiceScope CreateScope() => _scopeFactory.CreateScope();

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Start match", null, (_, _) => StartMatch());
        menu.Items.Add("Stop match", null, async (_, _) => await StopMatchAsync().ConfigureAwait(false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Show status", null, (_, _) => ShowWindow());
        menu.Items.Add("Hide status", null, (_, _) => HideWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        return menu;
    }

    private void StartMatch()
    {
        _coordinator.StartManualMatch();
        ShowWindow();
        _logger.LogInformation("Manual match session started from tray");
    }

    private async Task StopMatchAsync()
    {
        await _coordinator.ForceEndMatchAsync("Operator stop", CancellationToken.None).ConfigureAwait(false);
        _logger.LogInformation("Manual end-match triggered from tray");
    }

    private void ShowWindow()
    {
        if (_statusForm.Visible)
        {
            _statusForm.Activate();
            return;
        }

        _statusForm.Show();
        _statusForm.Activate();
    }

    private void HideWindow()
    {
        _statusForm.Hide();
    }

    private void Exit()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _statusForm.AllowClose = true;
        _statusForm.Close();
        ExitThread();
    }
}
