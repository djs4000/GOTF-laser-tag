using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LaserTag.Defusal.Domain;
using LaserTag.Defusal.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LaserTag.Defusal.Services;

/// <summary>
/// Encapsulates the foreground focus and input automation required to end a match.
/// </summary>
public sealed class FocusService : IFocusService
{
    private readonly ILogger<FocusService> _logger;
    private readonly UiAutomationOptions _options;
    private readonly Regex _titleRegex;
    private readonly string _targetProcessName;
    private readonly bool _isCurrentProcessElevated;
    private SynchronizationContext? _uiContext;

    public FocusService(IOptions<UiAutomationOptions> options, ILogger<FocusService> logger)
    {
        _logger = logger;
        _options = options.Value;
        _titleRegex = new Regex(_options.WindowTitleRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        _targetProcessName = Path.GetFileNameWithoutExtension(_options.ProcessName);
        _isCurrentProcessElevated = IsCurrentProcessElevated();
    }

    /// <summary>
    /// Binds the service to the UI thread so that automation is marshalled correctly.
    /// </summary>
    public void BindToUiThread(SynchronizationContext context)
    {
        _uiContext = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Attempts to focus the LT window and send the configured shortcut.
    /// </summary>
    public Task<FocusActionResult> TryEndMatchAsync(string reason, CancellationToken cancellationToken)
    {
        return DispatchAsync(reason, sendShortcut: true, cancellationToken);
    }

    public Task<FocusActionResult> TryFocusWindowAsync(string reason, CancellationToken cancellationToken)
    {
        return DispatchAsync(reason, sendShortcut: false, cancellationToken);
    }

    private Task<FocusActionResult> DispatchAsync(string reason, bool sendShortcut, CancellationToken cancellationToken)
    {
        if (_uiContext is { } context && SynchronizationContext.Current != context)
        {
            var tcs = new TaskCompletionSource<FocusActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Post(async _ =>
            {
                try
                {
                    var result = await ExecuteAsync(reason, sendShortcut, cancellationToken).ConfigureAwait(true);
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, null);
            return tcs.Task;
        }

        return ExecuteAsync(reason, sendShortcut, cancellationToken);
    }

    private async Task<FocusActionResult> ExecuteAsync(string reason, bool sendShortcut, CancellationToken cancellationToken)
    {
        var targetProcess = FindTargetProcess();
        if (targetProcess is null || targetProcess.MainWindowHandle == IntPtr.Zero)
        {
            var message = "Target window not found";
            _logger.LogWarning("{Message}. Ensure process {Process} with title {TitleRegex} is running.", message, _options.ProcessName, _options.WindowTitleRegex);
            return new FocusActionResult(false, message);
        }

        var elevationKnown = TryGetElevation(targetProcess, out var isTargetElevated);
        if (!elevationKnown)
        {
            _logger.LogDebug("Unable to determine elevation for process {ProcessId}", targetProcess.Id);
        }

        var handle = targetProcess.MainWindowHandle;
        NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);

        var currentThread = NativeMethods.GetCurrentThreadId();
        var targetThread = NativeMethods.GetWindowThreadProcessId(handle, out _);
        var foregroundHandle = NativeMethods.GetForegroundWindow();
        var foregroundThread = foregroundHandle != IntPtr.Zero ? NativeMethods.GetWindowThreadProcessId(foregroundHandle, out _) : 0;

        var attachedTarget = false;
        var attachedForeground = false;
        try
        {
            if (targetThread != currentThread)
            {
                attachedTarget = NativeMethods.AttachThreadInput(currentThread, targetThread, true);
            }
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attachedForeground = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
            }

            if (!NativeMethods.SetForegroundWindow(handle))
            {
                _logger.LogWarning("SetForegroundWindow failed with error {Error}", Marshal.GetLastPInvokeError());
                return new FocusActionResult(false, "Failed to focus target window");
            }

            await Task.Delay(_options.PostShortcutDelayMs, cancellationToken).ConfigureAwait(true);

            if (sendShortcut)
            {
                if (!await TrySendShortcutAsync(cancellationToken).ConfigureAwait(true))
                {
                    var failureDescription = !_isCurrentProcessElevated && elevationKnown && isTargetElevated
                        ? "Failed to send shortcut; target process is elevated—run this app as administrator"
                        : "Failed to send shortcut";
                    _logger.LogWarning("{Description} ({Reason})", failureDescription, reason);
                    return new FocusActionResult(false, failureDescription);
                }

                await Task.Delay(_options.PostShortcutDelayMs, cancellationToken).ConfigureAwait(true);

                var successDescription = $"Sent Ctrl+S at {DateTimeOffset.Now:HH:mm:ss}";
                _logger.LogInformation("{Description} due to {Reason}", successDescription, reason);
                return new FocusActionResult(true, successDescription);
            }
            else
            {
                var focusOnlyDescription = $"Focused target window at {DateTimeOffset.Now:HH:mm:ss}";
                _logger.LogInformation("{Description} ({Reason})", focusOnlyDescription, reason);
                return new FocusActionResult(true, focusOnlyDescription);
            }
        }
        finally
        {
            if (attachedTarget)
            {
                NativeMethods.AttachThreadInput(currentThread, targetThread, false);
            }

            if (attachedForeground)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    public FocusWindowInfo GetForegroundWindowInfo()
    {
        var foregroundHandle = NativeMethods.GetForegroundWindow();
        if (foregroundHandle == IntPtr.Zero)
        {
            return FocusWindowInfo.Empty;
        }

        var title = ReadWindowTitle(foregroundHandle);
        var isTarget = false;

        try
        {
            NativeMethods.GetWindowThreadProcessId(foregroundHandle, out var processId);
            if (processId != 0)
            {
                using var process = Process.GetProcessById((int)processId);
                if (string.Equals(process.ProcessName, _targetProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    isTarget = !string.IsNullOrEmpty(title) && _titleRegex.IsMatch(title);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve foreground process info for handle {Handle}", foregroundHandle);
        }

        return new FocusWindowInfo(foregroundHandle, title, isTarget);
    }

    private Process? FindTargetProcess()
    {
        var candidates = Process.GetProcessesByName(_targetProcessName);
        foreach (var process in candidates)
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                var title = process.MainWindowTitle;
                if (!string.IsNullOrEmpty(title) && _titleRegex.IsMatch(title))
                {
                    return process;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Unable to inspect process {ProcessId}", process.Id);
            }
        }

        return null;
    }

    private static string? ReadWindowTitle(IntPtr handle)
    {
        var length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0)
        {
            return null;
        }

        var buffer = new StringBuilder(length + 1);
        return NativeMethods.GetWindowText(handle, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : null;
    }

    private static Task<bool> TrySendShortcutAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            SendKeys.SendWait("^s");
            return Task.FromResult(true);
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(false);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }

    private static bool TryGetElevation(Process process, out bool isElevated)
    {
        isElevated = false;
        IntPtr tokenHandle = IntPtr.Zero;
        IntPtr buffer = IntPtr.Zero;

        try
        {
            if (!NativeMethods.OpenProcessToken(process.Handle, NativeMethods.TOKEN_QUERY, out tokenHandle))
            {
                return false;
            }

            var elevationSize = Marshal.SizeOf<NativeMethods.TOKEN_ELEVATION>();
            buffer = Marshal.AllocHGlobal(elevationSize);

            if (!NativeMethods.GetTokenInformation(tokenHandle, NativeMethods.TokenElevation, buffer, elevationSize, out _))
            {
                return false;
            }

            var elevation = Marshal.PtrToStructure<NativeMethods.TOKEN_ELEVATION>(buffer);
            isElevated = elevation.TokenIsElevated != 0;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }

            if (tokenHandle != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(tokenHandle);
            }
        }
    }

    private static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

}
