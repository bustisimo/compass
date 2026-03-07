using System.Collections.ObjectModel;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Compass.ViewModels;

public class SpotlightViewModel : ViewModelBase
{
    private readonly IAppSearchService _searchService;
    private readonly IGeminiService _geminiService;
    private readonly ISettingsService _settingsService;
    private readonly IExtensionService _extensionService;
    private readonly IModelRoutingService _routingService;
    private readonly ILogger<SpotlightViewModel> _logger;

    private string _searchText = "";
    private AppSearchResult? _selectedResult;
    private bool _isChatMode;
    private bool _isPinned;
    private bool _isInputEnabled = true;

    public SpotlightViewModel(
        IAppSearchService searchService,
        IGeminiService geminiService,
        ISettingsService settingsService,
        IExtensionService extensionService,
        IModelRoutingService routingService,
        ILogger<SpotlightViewModel> logger)
    {
        _searchService = searchService;
        _geminiService = geminiService;
        _settingsService = settingsService;
        _extensionService = extensionService;
        _routingService = routingService;
        _logger = logger;
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public AppSearchResult? SelectedResult
    {
        get => _selectedResult;
        set => SetProperty(ref _selectedResult, value);
    }

    public ObservableCollection<AppSearchResult> SearchResults { get; } = new();

    public bool IsChatMode
    {
        get => _isChatMode;
        set => SetProperty(ref _isChatMode, value);
    }

    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    public bool IsInputEnabled
    {
        get => _isInputEnabled;
        set => SetProperty(ref _isInputEnabled, value);
    }

    public bool HasChatHistory => _geminiService.HasHistory;

    public bool ShowResumeIndicator => HasChatHistory && !IsChatMode && string.IsNullOrEmpty(SearchText);

    public void TogglePin()
    {
        IsPinned = !IsPinned;
    }

    public void NotifyResumeIndicatorChanged()
    {
        OnPropertyChanged(nameof(ShowResumeIndicator));
        OnPropertyChanged(nameof(HasChatHistory));
    }
}
