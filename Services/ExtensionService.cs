using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Compass.Services;

public class ExtensionService
{
    public readonly string ExtensionsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Compass", "Extensions");

    public void EnsureExtensionsFolderExists()
    {
        if (!Directory.Exists(ExtensionsPath))
            Directory.CreateDirectory(ExtensionsPath);
    }

    public List<CompassExtension> LoadExtensions()
    {
        var result = new List<CompassExtension>();
        if (!Directory.Exists(ExtensionsPath)) return result;

        foreach (var file in Directory.GetFiles(ExtensionsPath, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var ext = JsonSerializer.Deserialize<CompassExtension>(json);
                if (ext != null) result.Add(ext);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Compass] LoadExtensions '{file}': {ex.Message}");
            }
        }
        return result;
    }

    public void SaveExtension(CompassExtension ext)
    {
        EnsureExtensionsFolderExists();
        string json = JsonSerializer.Serialize(ext, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(ExtensionsPath, $"{ext.TriggerName}.json"), json);
    }

    public void DeleteExtension(string triggerName)
    {
        string path = Path.Combine(ExtensionsPath, $"{triggerName}.json");
        if (File.Exists(path))
        {
            try { File.Delete(path); }
            catch (Exception ex) { Debug.WriteLine($"[Compass] DeleteExtension '{triggerName}': {ex.Message}"); }
        }
    }

    public void ExecuteExtension(CompassExtension ext)
    {
        string script = ext.PowerShellScript.Replace("\"", "\\\"");
        var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        Process.Start(psi);
    }
}
