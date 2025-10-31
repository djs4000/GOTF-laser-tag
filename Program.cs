using System.Reflection;
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

        builder.Services.AddSingleton<CidrAllowlistService>();
        builder.Services.AddSingleton<RelayService>();
        builder.Services.AddSingleton<MatchCoordinator>();
        builder.Services.AddSingleton<IFocusService, FocusService>();
        builder.Services.AddSingleton<StatusForm>();
        builder.Services.AddSingleton<TrayApplicationContext>();

        var httpOptions = builder.Configuration.GetSection("Http").Get<HttpOptions>() ?? new HttpOptions();
        if (httpOptions.Urls.Length == 0)
        {
            httpOptions.Urls = new[] { "http://127.0.0.1:5055" };
        }

        builder.WebHost.UseUrls(httpOptions.Urls);

        var app = builder.Build();

        app.UseMiddleware<SecurityMiddleware>();

        app.MapPut("/prop/status", async (PropStatusDto dto, MatchCoordinator coordinator, CancellationToken cancellationToken) =>
        {
            await coordinator.UpdatePropAsync(dto, cancellationToken).ConfigureAwait(false);
            return Results.Accepted();
        });

        app.MapPut("/match/clock", async (MatchClockDto dto, MatchCoordinator coordinator, CancellationToken cancellationToken) =>
        {
            await coordinator.UpdateMatchClockAsync(dto, cancellationToken).ConfigureAwait(false);
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
}
