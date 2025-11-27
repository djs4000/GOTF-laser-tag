using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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
    private SynchronizationContext? _uiContext;

    public FocusService(IOptions<UiAutomationOptions> options, ILogger<FocusService> logger)
    {
        _logger = logger;
        _options = options.Value;
        _titleRegex = new Regex(_options.WindowTitleRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        _targetProcessName = Path.GetFileNameWithoutExtension(_options.ProcessName);
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
                if (!TrySendShortcut(out var errorMessage, out var lastError))
                {
                    var description = $"Failed to send shortcut: {errorMessage}; win32={lastError}";
                    _logger.LogWarning("{Description} ({Reason})", description, reason);
                    return new FocusActionResult(false, description);
                }

                await Task.Delay(_options.PostShortcutDelayMs, cancellationToken).ConfigureAwait(true);

                var description = $"Sent Ctrl+S at {DateTimeOffset.Now:HH:mm:ss}";
                _logger.LogInformation("{Description} due to {Reason}", description, reason);
                return new FocusActionResult(true, description);
            }
            else
            {
                var description = $"Focused target window at {DateTimeOffset.Now:HH:mm:ss}";
                _logger.LogInformation("{Description} ({Reason})", description, reason);
                return new FocusActionResult(true, description);
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

    private static bool TrySendShortcut(out string? errorMessage, out int lastError)
    {
        var inputs = new[]
        {
            CreateKeyDown(NativeMethods.VK_CONTROL),
            CreateKeyDown(NativeMethods.VK_S),
            CreateKeyUp(NativeMethods.VK_S),
            CreateKeyUp(NativeMethods.VK_CONTROL)
        };

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
        {
            errorMessage = $"SendInput sent {sent}/{inputs.Length} inputs";
            lastError = Marshal.GetLastPInvokeError();
            return false;
        }

        errorMessage = null;
        lastError = 0;
        return true;
    }

    private static NativeMethods.INPUT CreateKeyDown(ushort key)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = key,
                    dwFlags = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private static NativeMethods.INPUT CreateKeyUp(ushort key)
    {
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = key,
                    dwFlags = NativeMethods.KEYEVENTF_KEYUP,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

}
