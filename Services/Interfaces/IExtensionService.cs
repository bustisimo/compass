namespace Compass.Services.Interfaces;

public interface IExtensionService
{
    string ExtensionsPath { get; }
    void EnsureExtensionsFolderExists();
    List<CompassExtension> LoadExtensions();
    void SaveExtension(CompassExtension ext);
    void DeleteExtension(string triggerName);
    string ExecuteExtension(CompassExtension ext);
}
