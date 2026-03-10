using System.Collections.ObjectModel;
using System.Windows.Input;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Compass.ViewModels;

public class ChatViewModel : ViewModelBase
{
    private readonly IGeminiService _geminiService;
    private readonly ILogger<ChatViewModel> _logger;

    public ChatViewModel(IGeminiService geminiService, ILogger<ChatViewModel> logger)
    {
        _geminiService = geminiService;
        _logger = logger;

        ClearCommand = new RelayCommand(Clear);
    }

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public ICommand ClearCommand { get; }

    public void AddMessage(ChatMessage message)
    {
        Messages.Add(message);
        OnPropertyChanged(nameof(Messages));
    }

    public void Clear()
    {
        Messages.Clear();
        _geminiService.ClearHistory();
        _logger.LogInformation("Chat cleared");
    }

    public List<(string role, string text)> GetExportableHistory()
    {
        return _geminiService.GetExportableHistory();
    }
}
