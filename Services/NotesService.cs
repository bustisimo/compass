using System.IO;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

/// <summary>
/// Simple persistence for quick notes/scratchpad.
/// </summary>
public class NotesService
{
    private readonly string _notesPath;
    private readonly ILogger<NotesService> _logger;

    public NotesService(ILogger<NotesService> logger)
    {
        _logger = logger;
        _notesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Compass", "notes.txt");
    }

    public string LoadNotes()
    {
        try
        {
            if (File.Exists(_notesPath))
                return File.ReadAllText(_notesPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load notes");
        }
        return "";
    }

    public void SaveNotes(string content)
    {
        try
        {
            var dir = Path.GetDirectoryName(_notesPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_notesPath, content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save notes");
        }
    }
}
