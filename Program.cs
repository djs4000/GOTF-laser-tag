using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using LaserTag.Defusal.Domain;
using LaserTag.Defusal.Http;
using LaserTag.Defusal.Services;
using LaserTag.Defusal.Ui;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args);

        builder.Services.Configure<HttpOptions>(builder.Configuration.GetSection("Http"));
        builder.Services.Configure<RelayOptions>(builder.Configuration.GetSection("Relay"));
        builder.Services.Configure<MatchOptions>(builder.Configuration.GetSection("Match"));
        builder.Services.Configure<UiAutomationOptions>(builder.Configuration.GetSection("UiAutomation"));
        builder.Services.Configure<DiagnosticsOptions>(builder.Configuration.GetSection("Diagnostics"));

        builder.Host.UseSerilog((context, services, configuration) =>
        {
            var diagnostics = context.Configuration.GetSection("Diagnostics").Get<DiagnosticsOptions>() ?? new DiagnosticsOptions();
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .MinimumLevel.Is(Enum.TryParse<LogEventLevel>(diagnostics.LogLevel, true, out var level) ? level : LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Console();

            if (diagnostics.WriteToFile && !string.IsNullOrWhiteSpace(diagnostics.LogPath))
            {
                configuration.WriteTo.File(diagnostics.LogPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7);
            }
        });

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        });

        builder.Services.AddSingleton<CidrAllowlistService>();
        builder.Services.AddSingleton<RelayService>();
        builder.Services.AddSingleton<MatchCoordinator>();
        builder.Services.AddSingleton<IFocusService, FocusService>();

        var httpOptions = builder.Configuration.GetSection("Http").Get<HttpOptions>() ?? new HttpOptions();
        var resolvedUrls = ResolveBindableUrls(httpOptions);
        builder.WebHost.UseUrls(resolvedUrls);
        Console.WriteLine($"[config] Binding HTTP server to: {string.Join(", ", resolvedUrls)}");

        builder.Services.AddSingleton(new HttpEndpointMetadata(resolvedUrls));
        builder.Services.AddSingleton<StatusForm>();
        builder.Services.AddSingleton<TrayApplicationContext>();

        var app = builder.Build();

        app.UseMiddleware<SecurityMiddleware>();

        app.MapPost("/prop", async (PropStatusDto dto, IOptions<HttpOptions> httpOptions, MatchCoordinator coordinator, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            await coordinator.UpdatePropAsync(dto, cancellationToken).ConfigureAwait(false);
            StampAckHeaders(httpContext.Response, "prop-status");
            var includeDiagnostics = ShouldIncludeDiagnostics(httpContext.Request, httpOptions.Value.DiagnosticsToken);
            var response = coordinator.BuildPropResponse(dto.Timestamp, includeDiagnostics);
            return Results.Ok(response);
        });

        app.MapPost("/match", async (MatchSnapshotDto dto, MatchCoordinator coordinator, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            await coordinator.UpdateMatchSnapshotAsync(dto, cancellationToken).ConfigureAwait(false);
            StampAckHeaders(httpContext.Response, "match-clock");
            return Results.Accepted();
        });

        app.MapGet("/healthz", () => Results.Ok(new
        {
            status = "ok",
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev"
        }));

        var serverTask = app.RunAsync();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var trayContext = app.Services.GetRequiredService<TrayApplicationContext>();
        Application.Run(trayContext);

        await app.StopAsync();
        await serverTask.ConfigureAwait(false);
        await app.DisposeAsync();
        Log.CloseAndFlush();
    }

    private static string[] ResolveBindableUrls(HttpOptions httpOptions)
    {
        static HashSet<IPAddress> GetActiveUnicastAddresses()
        {
            var addresses = new HashSet<IPAddress>();

            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    addresses.Add(unicast.Address);
                }
            }

            return addresses;
        }

        static bool IsBindable(IPAddress address, HashSet<IPAddress> activeAddresses)
        {
            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            if (IPAddress.Any.Equals(address) || IPAddress.IPv6Any.Equals(address))
            {
                return true;
            }

            return activeAddresses.Contains(address);
        }

        var configuredUrls = httpOptions.Urls ?? Array.Empty<string>();
        var activeAddresses = GetActiveUnicastAddresses();
        var validUrls = new List<string>();

        foreach (var configuredUrl in configuredUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
        {
            if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            {
                Console.WriteLine($"[config] Ignoring invalid HTTP Url '{configuredUrl}'.");
                continue;
            }

            if (IPAddress.TryParse(uri.Host, out var ipAddress))
            {
                if (!IsBindable(ipAddress, activeAddresses))
                {
                    Console.WriteLine($"[config] Skipping HTTP Url '{configuredUrl}' because {ipAddress} is not assigned to this machine.");
                    continue;
                }
            }

            validUrls.Add(configuredUrl);
        }

        if (validUrls.Count == 0)
        {
            const string fallback = "http://127.0.0.1:5055";
            Console.WriteLine($"[config] No valid HTTP Urls configured. Falling back to '{fallback}'.");
            validUrls.Add(fallback);
        }

        return validUrls.ToArray();
    }

    private static void StampAckHeaders(HttpResponse response, string ackValue)
    {
        response.Headers["X-Defusal-Ack"] = ackValue;
        response.Headers["X-Defusal-Received-At"] = DateTimeOffset.UtcNow
            .ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);
    }

    private static bool ShouldIncludeDiagnostics(HttpRequest request, string? diagnosticsToken)
    {
        if (string.IsNullOrWhiteSpace(diagnosticsToken))
        {
            return false;
        }

        if (request.Headers.TryGetValue("X-Debug-Token", out var headerValues) && headerValues.Any(v => string.Equals(v, diagnosticsToken, StringComparison.Ordinal)))
        {
            return true;
        }

        if (request.Query.TryGetValue("debug_token", out var queryValues) && queryValues.Any(v => string.Equals(v, diagnosticsToken, StringComparison.Ordinal)))
        {
            return true;
        }

        return false;
    }

}
