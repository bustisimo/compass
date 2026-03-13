using System.IO;
using System.Windows;
using Compass.Plugins;
using Compass.Services;
using Compass.Services.Interfaces;
using Compass.Services.Providers;
using Compass.Themes;
using Compass.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Compass;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers
        DispatcherUnhandledException += (s, args) =>
        {
            Log.Fatal(args.Exception, "Unhandled UI exception");
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{args.Exception.Message}\n\nThe error has been logged.",
                "Compass Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Log.Fatal(ex, "Unhandled domain exception (isTerminating={IsTerminating})", args.IsTerminating);
        };
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        // Configure Serilog
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Compass", "Logs", "compass-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // Build DI container
        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        // Core services
        services.AddSingleton<ICredentialService, CredentialService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IGeminiService, GeminiService>();
        services.AddSingleton<IExtensionService, ExtensionService>();
        services.AddSingleton<FrecencyTracker>();
        services.AddSingleton<IAppSearchService, AppSearchService>();
        services.AddSingleton<ISystemCommandService, SystemCommandService>();
        services.AddSingleton<IModelRoutingService, ModelRoutingService>();

        // AI providers
        services.AddSingleton<GeminiProvider>();
        services.AddSingleton<OpenAiProvider>();
        services.AddSingleton<AnthropicProvider>();
        services.AddSingleton<OllamaProvider>();
        services.AddSingleton<AiProviderRegistry>();

        // Chat history
        services.AddSingleton<ChatHistoryService>();

        // File search
        services.AddSingleton<FileIndexService>();

        // Calculator
        services.AddSingleton<CalculatorService>();

        // Clipboard history
        services.AddSingleton<ClipboardHistoryService>();

        // Snippets
        services.AddSingleton<SnippetService>();

        // PowerShell sandbox
        services.AddSingleton<PowerShellSandbox>();

        // Themes
        services.AddSingleton<ThemeManager>();

        // Plugin host
        services.AddSingleton<PluginHost>();

        // Update service
        services.AddSingleton<UpdateService>();

        // Settings sync
        services.AddSingleton<SettingsSyncService>();

        // Quick actions
        services.AddSingleton<QuickActionsService>();

        // Notes
        services.AddSingleton<NotesService>();

        // Widgets
        services.AddSingleton<IWidgetService, WidgetService>();
        services.AddSingleton<WeatherService>();

        // Additional services
        services.AddSingleton<FileContentSearchService>();
        services.AddSingleton<BookmarkService>();
        services.AddSingleton<RecentFilesService>();
        services.AddSingleton<RagService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<CalendarService>();
        services.AddSingleton<MediaSessionService>();

        // ViewModels
        services.AddSingleton<SpotlightViewModel>();
        services.AddSingleton<ChatViewModel>();
        services.AddSingleton<ManagerViewModel>();

        // MainWindow
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Compass starting up");

        // Register plugins (lightweight — just adds to list)
        var pluginHost = _serviceProvider.GetRequiredService<PluginHost>();
        pluginHost.Register(new CalculatorPlugin(_serviceProvider.GetRequiredService<CalculatorService>()));
        pluginHost.Register(new ClipboardPlugin(_serviceProvider.GetRequiredService<ClipboardHistoryService>()));
        pluginHost.Register(new SnippetPlugin(_serviceProvider.GetRequiredService<SnippetService>()));
        pluginHost.Register(new FileSearchPlugin(
            _serviceProvider.GetRequiredService<FileContentSearchService>(),
            _serviceProvider.GetRequiredService<ILogger<FileSearchPlugin>>()));
        pluginHost.Register(new BookmarkPlugin(
            _serviceProvider.GetRequiredService<BookmarkService>(),
            _serviceProvider.GetRequiredService<ILogger<BookmarkPlugin>>()));
        pluginHost.Register(new RecentFilesPlugin(
            _serviceProvider.GetRequiredService<RecentFilesService>(),
            _serviceProvider.GetRequiredService<ILogger<RecentFilesPlugin>>()));
        pluginHost.Register(new QuickTogglePlugin(_serviceProvider.GetRequiredService<ISystemCommandService>()));

        // Show the window first, then stagger heavy init to avoid 100% CPU spike
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        var appSettings = settingsService.LoadSettings();

        _ = SafeFireAndForget(StaggeredInitAsync(pluginHost, appSettings, logger), logger, "Staggered initialization");
    }

    /// <summary>
    /// Staggers heavy startup work so it doesn't all compete for CPU simultaneously.
    /// </summary>
    private async Task StaggeredInitAsync(PluginHost pluginHost, AppSettings appSettings, Microsoft.Extensions.Logging.ILogger logger)
    {
        // Phase 1: Plugin initialization (bookmarks, recent files, etc.)
        await pluginHost.InitializeAllAsync();

        // Brief pause to let CPU settle before next heavy phase
        await Task.Delay(500);

        // Phase 2: File content search indexing (if enabled)
        if (appSettings.FileContentSearchEnabled && appSettings.FileSearchDirectories.Count > 0)
        {
            var fileSearchService = _serviceProvider!.GetRequiredService<FileContentSearchService>();
            fileSearchService.StartIndexing(appSettings.FileSearchDirectories, appSettings.FileContentSearchExtensions);
            await Task.Delay(500);
        }

        // Phase 3: RAG indexing (if enabled) — heaviest operation, runs last
        if (appSettings.RagEnabled && appSettings.RagDirectories.Count > 0)
        {
            var ragService = _serviceProvider!.GetRequiredService<RagService>();
            ragService.StartIndexing(appSettings.RagDirectories);
        }
    }

    private static async Task SafeFireAndForget(Task task, Microsoft.Extensions.Logging.ILogger logger, string context)
    {
        try { await task; }
        catch (Exception ex) { logger.LogError(ex, "Fire-and-forget failed: {Context}", context); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
