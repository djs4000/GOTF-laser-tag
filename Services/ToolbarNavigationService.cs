using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LaserTag.Defusal.Services;

/// <summary>
/// Centralizes toolbar-driven navigation so every utility form is resolved through DI,
/// opened as an owned window, and keeps the StatusForm visible/activated.
/// </summary>
public sealed class ToolbarNavigationService : IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ToolbarNavigationService> _logger;
    private readonly Dictionary<Type, NavigationEntry> _openForms = new();
    private readonly object _sync = new();
    private bool _disposed;

    public ToolbarNavigationService(IServiceScopeFactory scopeFactory, ILogger<ToolbarNavigationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Restores the StatusForm and opens/focuses the requested utility form.
    /// </summary>
    public void ShowOwnedForm<TForm>(Form owner, Action<TForm>? configure = null)
        where TForm : Form
    {
        EnsureUiThread(owner, () =>
        {
            RestoreStatusSurface(owner);

            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                var formType = typeof(TForm);
                if (_openForms.TryGetValue(formType, out var existing))
                {
                    FocusForm(existing.Form);
                    return;
                }

                var scope = _scopeFactory.CreateScope();
                try
                {
                    var form = scope.ServiceProvider.GetRequiredService<TForm>();
                    configure?.Invoke(form);
                    FormClosedEventHandler? handler = null;
                    handler = (_, _) =>
                    {
                        form.FormClosed -= handler!;
                        ReleaseForm(formType);
                    };
                    form.FormClosed += handler;
                    _openForms[formType] = new NavigationEntry(form, scope);
                    form.Show(owner);
                    FocusForm(form);
                    _logger.LogInformation("Opened {FormName} via toolbar navigation.", formType.Name);
                }
                catch
                {
                    scope.Dispose();
                    throw;
                }
            }
        });
    }

    /// <summary>
    /// Closes any owned dialog of the specified type if present.
    /// </summary>
    public void CloseForm<TForm>(Form owner)
        where TForm : Form
    {
        EnsureUiThread(owner, () =>
        {
            lock (_sync)
            {
                if (_openForms.TryGetValue(typeof(TForm), out var entry))
                {
                    entry.Form.Close();
                }
            }
        });
    }

    private void ReleaseForm(Type formType)
    {
        lock (_sync)
        {
            if (_openForms.Remove(formType, out var entry))
            {
                entry.Scope.Dispose();
                _logger.LogInformation("{FormName} closed; disposed scope.", formType.Name);
            }
        }
    }

    private static void RestoreStatusSurface(Form owner)
    {
        if (owner.IsDisposed)
        {
            return;
        }

        if (!owner.Visible)
        {
            owner.Show();
        }

        owner.BringToFront();
        owner.Activate();
    }

    private static void FocusForm(Form form)
    {
        if (form.WindowState == FormWindowState.Minimized)
        {
            form.WindowState = FormWindowState.Normal;
        }

        form.BringToFront();
        form.Activate();
    }

    private static void EnsureUiThread(Control owner, Action action)
    {
        if (owner.InvokeRequired)
        {
            owner.BeginInvoke(action);
            return;
        }

        action();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var entry in _openForms.Values)
            {
                entry.Form.Dispose();
                entry.Scope.Dispose();
            }

            _openForms.Clear();
        }
    }

    private sealed record NavigationEntry(Form Form, IServiceScope Scope);
}
