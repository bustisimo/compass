using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class ChatHistoryService
{
    private static readonly string ChatHistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Compass", "ChatHistory");

    private readonly ILogger<ChatHistoryService> _logger;

    public ChatHistoryService(ILogger<ChatHistoryService> logger)
    {
        _logger = logger;
        Directory.CreateDirectory(ChatHistoryPath);
    }

    public void SaveSession(ChatSession session)
    {
        try
        {
            session.LastMessageAt = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(session.Title) && session.Messages.Count > 0)
            {
                var firstUserMsg = session.Messages.FirstOrDefault(m => m.IsUser);
                session.Title = firstUserMsg?.Text.Length > 50
                    ? firstUserMsg.Text[..50] + "..."
                    : firstUserMsg?.Text ?? "Chat";
            }

            string json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(ChatHistoryPath, $"{session.Id}.json"), json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save chat session {SessionId}", session.Id);
        }
    }

    public ChatSession? LoadSession(string sessionId)
    {
        try
        {
            string path = Path.Combine(ChatHistoryPath, $"{sessionId}.json");
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ChatSession>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load chat session {SessionId}", sessionId);
            return null;
        }
    }

    public List<ChatSession> ListSessions()
    {
        var sessions = new List<ChatSession>();
        try
        {
            foreach (var file in Directory.GetFiles(ChatHistoryPath, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var session = JsonSerializer.Deserialize<ChatSession>(json);
                    if (session != null) sessions.Add(session);
                }
                catch { /* skip corrupt files */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list chat sessions");
        }
        return sessions.OrderByDescending(s => s.LastMessageAt).ToList();
    }

    public List<ChatSession> SearchSessions(string query)
    {
        return ListSessions()
            .Where(s => s.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        s.Messages.Any(m => m.Text.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public void DeleteSession(string sessionId)
    {
        try
        {
            string path = Path.Combine(ChatHistoryPath, $"{sessionId}.json");
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete chat session {SessionId}", sessionId);
        }
    }
}
