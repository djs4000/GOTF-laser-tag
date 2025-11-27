using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace LaserTag.Defusal.Services;

internal sealed class HardwareShortcutSender : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly object? _driver;
    private readonly object? _keyboard;
    private readonly MethodInfo? _keyDown;
    private readonly MethodInfo? _keyUp;
    private readonly object? _ctrlKey;
    private readonly object? _sKey;
    private readonly MethodInfo? _disposeAsync;
    private readonly MethodInfo? _dispose;

    private HardwareShortcutSender(
        ILogger logger,
        object? driver,
        object? keyboard,
        MethodInfo? keyDown,
        MethodInfo? keyUp,
        object? ctrlKey,
        object? sKey,
        MethodInfo? disposeAsync,
        MethodInfo? dispose)
    {
        _logger = logger;
        _driver = driver;
        _keyboard = keyboard;
        _keyDown = keyDown;
        _keyUp = keyUp;
        _ctrlKey = ctrlKey;
        _sKey = sKey;
        _disposeAsync = disposeAsync;
        _dispose = dispose;
    }

    public bool IsReady => _driver is not null && _keyboard is not null && _keyDown is not null && _keyUp is not null && _ctrlKey is not null && _sKey is not null;

    public static async Task<HardwareShortcutSender> CreateAsync(ILogger logger, CancellationToken cancellationToken)
    {
        var assembly = TryLoadFakerInputAssembly();
        if (assembly is null)
        {
            logger.LogWarning("FakerInput/ViGEmBus client assembly not found; falling back to SendInput");
            return new HardwareShortcutSender(logger, null, null, null, null, null, null, null, null);
        }

        var driverType = FindDriverType(assembly);
        if (driverType is null)
        {
            logger.LogWarning("No FakerInput driver type located; falling back to SendInput");
            return new HardwareShortcutSender(logger, null, null, null, null, null, null, null, null);
        }

        object? driver = null;
        try
        {
            driver = await CreateDriverAsync(driverType, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to initialise FakerInput driver; falling back to SendInput");
        }

        var keyboard = driver is null ? null : driverType.GetProperty("Keyboard", BindingFlags.Instance | BindingFlags.Public)?.GetValue(driver);
        var keyboardType = keyboard?.GetType();

        var keyEnum = assembly.GetTypes().FirstOrDefault(t => t.IsEnum && MatchesKeys(t));
        var ctrlKey = keyEnum is null ? null : ResolveKey(keyEnum, "LeftControl", "LeftCtrl", "Control", "Ctrl");
        var sKey = keyEnum is null ? null : ResolveKey(keyEnum, "S");

        var keyDown = keyboardType is null || keyEnum is null
            ? null
            : FindKeyMethod(keyboardType, new[] { "KeyDownAsync", "KeyDown", "PressKeyDownAsync" }, keyEnum);
        var keyUp = keyboardType is null || keyEnum is null
            ? null
            : FindKeyMethod(keyboardType, new[] { "KeyUpAsync", "KeyUp", "PressKeyUpAsync" }, keyEnum);

        var disposeAsync = driverType?.GetMethod("DisposeAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var dispose = driverType?.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        return new HardwareShortcutSender(logger, driver, keyboard, keyDown, keyUp, ctrlKey, sKey, disposeAsync, dispose);
    }

    public async Task<ShortcutSendResult> TrySendCtrlSAsync(CancellationToken cancellationToken)
    {
        if (!IsReady)
        {
            return new ShortcutSendResult(false, "Hardware sender not available", null);
        }

        try
        {
            await InvokeKeyAsync(_keyDown!, _keyboard!, _ctrlKey!, cancellationToken).ConfigureAwait(false);
            await InvokeKeyAsync(_keyDown!, _keyboard!, _sKey!, cancellationToken).ConfigureAwait(false);
            await InvokeKeyAsync(_keyUp!, _keyboard!, _sKey!, cancellationToken).ConfigureAwait(false);
            await InvokeKeyAsync(_keyUp!, _keyboard!, _ctrlKey!, cancellationToken).ConfigureAwait(false);
            return new ShortcutSendResult(true, null, "ViGEmBus/HidHide (FakerInput)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Virtual HID keystroke failed");
            return new ShortcutSendResult(false, ex.Message, null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_driver is null)
        {
            return;
        }

        if (_disposeAsync is not null)
        {
            var disposeResult = _disposeAsync.Invoke(_driver, Array.Empty<object?>());
            await AwaitResultAsync(disposeResult).ConfigureAwait(false);
            return;
        }

        _dispose?.Invoke(_driver, Array.Empty<object?>());
    }

    private static Assembly? TryLoadFakerInputAssembly()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name?.Contains("FakerInput", StringComparison.OrdinalIgnoreCase) == true);
        if (loaded is not null)
        {
            return loaded;
        }

        try
        {
            return Assembly.Load("Nefarius.Utilities.FakerInput");
        }
        catch
        {
            return null;
        }
    }

    private static Type? FindDriverType(Assembly assembly)
    {
        return assembly.GetTypes()
            .FirstOrDefault(t => !t.IsAbstract && t.GetProperty("Keyboard", BindingFlags.Instance | BindingFlags.Public) is not null);
    }

    private static bool MatchesKeys(Type enumType)
    {
        var names = Enum.GetNames(enumType);
        return names.Contains("S") && names.Any(n => n is "LeftControl" or "LeftCtrl" or "Control" or "Ctrl");
    }

    private static object? ResolveKey(Type enumType, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (Enum.IsDefined(enumType, candidate))
            {
                return Enum.Parse(enumType, candidate);
            }
        }

        return null;
    }

    private static MethodInfo? FindKeyMethod(Type keyboardType, string[] names, Type keyEnum)
    {
        return keyboardType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m => names.Contains(m.Name) &&
                                  m.GetParameters().Length >= 1 &&
                                  m.GetParameters()[0].ParameterType == keyEnum);
    }

    private static async Task<object?> CreateDriverAsync(Type driverType, CancellationToken cancellationToken)
    {
        if (driverType.GetMethod("CreateAsync", BindingFlags.Public | BindingFlags.Static) is { } createAsync)
        {
            var result = createAsync.Invoke(null, Array.Empty<object?>());
            return await AwaitResultAsync(result).ConfigureAwait(false);
        }

        var driver = Activator.CreateInstance(driverType);
        var connectAsync = driverType.GetMethod("ConnectAsync", BindingFlags.Instance | BindingFlags.Public);
        if (connectAsync is not null)
        {
            var result = connectAsync.GetParameters().Length == 1
                ? connectAsync.Invoke(driver, new object?[] { cancellationToken })
                : connectAsync.Invoke(driver, Array.Empty<object?>());
            await AwaitResultAsync(result).ConfigureAwait(false);
        }

        return driver;
    }

    private static async Task<object?> AwaitResultAsync(object? result)
    {
        switch (result)
        {
            case null:
                return null;
            case Task task:
                await task.ConfigureAwait(false);
                return task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(task);
            default:
                var resultProperty = result.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
                if (resultProperty?.GetValue(result) is Task nestedTask)
                {
                    await nestedTask.ConfigureAwait(false);
                    return nestedTask.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(nestedTask);
                }

                return resultProperty?.GetValue(result) ?? result;
        }
    }

    private static async Task InvokeKeyAsync(MethodInfo method, object keyboard, object key, CancellationToken cancellationToken)
    {
        var parameters = method.GetParameters();
        object?[] args = parameters.Length switch
        {
            1 => new[] { key },
            2 when parameters[1].ParameterType == typeof(CancellationToken) => new object?[] { key, cancellationToken },
            _ => new[] { key }
        };

        var result = method.Invoke(keyboard, args);
        await AwaitResultAsync(result).ConfigureAwait(false);
    }
}

internal readonly record struct ShortcutSendResult(bool Succeeded, string? ErrorMessage, string? PathHint);
