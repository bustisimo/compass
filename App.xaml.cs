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

        // Phase 6: AI providers
        services.AddSingleton<GeminiProvider>();
        services.AddSingleton<OpenAiProvider>();
        services.AddSingleton<AnthropicProvider>();
        services.AddSingleton<OllamaProvider>();
        services.AddSingleton<AiProviderRegistry>();

        // Phase 7: Chat history
        services.AddSingleton<ChatHistoryService>();

        // Phase 8: File search
        services.AddSingleton<FileIndexService>();

        // Phase 9: Calculator
        services.AddSingleton<CalculatorService>();

        // Phase 10: Clipboard history
        services.AddSingleton<ClipboardHistoryService>();

        // Phase 11: Snippets
        services.AddSingleton<SnippetService>();

        // Phase 12: PowerShell sandbox
        services.AddSingleton<PowerShellSandbox>();

        // Phase 13: Themes
        services.AddSingleton<ThemeManager>();

        // Phase 16: Plugin host
        services.AddSingleton<PluginHost>();

        // Phase 17: Update service
        services.AddSingleton<UpdateService>();

        // Phase 18: Settings sync
        services.AddSingleton<SettingsSyncService>();

        // Phase 19: Widgets
        services.AddSingleton<IWidgetService, WidgetService>();
        services.AddSingleton<WeatherService>();

        // New services for feature expansion
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

        // Activate plugin system (Wave 0A)
        var pluginHost = _serviceProvider.GetRequiredService<PluginHost>();
        pluginHost.Register(new CalculatorPlugin(_serviceProvider.GetRequiredService<CalculatorService>()));
        pluginHost.Register(new ClipboardPlugin(_serviceProvider.GetRequiredService<ClipboardHistoryService>()));
        pluginHost.Register(new SnippetPlugin(_serviceProvider.GetRequiredService<SnippetService>()));
        pluginHost.Register(new FileSearchPlugin(_serviceProvider.GetRequiredService<FileContentSearchService>()));
        pluginHost.Register(new BookmarkPlugin(_serviceProvider.GetRequiredService<BookmarkService>()));
        pluginHost.Register(new RecentFilesPlugin(_serviceProvider.GetRequiredService<RecentFilesService>()));
        pluginHost.Register(new QuickTogglePlugin(_serviceProvider.GetRequiredService<ISystemCommandService>()));
        _ = SafeFireAndForget(pluginHost.InitializeAllAsync(), logger, "Plugin initialization");

        // Initialize file content search if enabled
        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        var appSettings = settingsService.LoadSettings();
        if (appSettings.FileContentSearchEnabled && appSettings.FileSearchDirectories.Count > 0)
        {
            var fileSearchService = _serviceProvider.GetRequiredService<FileContentSearchService>();
            fileSearchService.StartIndexing(appSettings.FileSearchDirectories, appSettings.FileContentSearchExtensions);
        }

        // Initialize RAG if enabled
        if (appSettings.RagEnabled && appSettings.RagDirectories.Count > 0)
        {
            var ragService = _serviceProvider.GetRequiredService<RagService>();
            ragService.StartIndexing(appSettings.RagDirectories);
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
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
