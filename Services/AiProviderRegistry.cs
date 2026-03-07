using Compass.Services.Interfaces;

namespace Compass.Services;

public class AiProviderRegistry
{
    private readonly Dictionary<string, IAiProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private string _activeProviderName = "Gemini";

    public void Register(IAiProvider provider)
    {
        _providers[provider.Name] = provider;
    }

    public IAiProvider? GetProvider(string name)
    {
        _providers.TryGetValue(name, out var provider);
        return provider;
    }

    public IAiProvider? ActiveProvider => GetProvider(_activeProviderName);

    public string ActiveProviderName
    {
        get => _activeProviderName;
        set => _activeProviderName = value;
    }

    public IEnumerable<string> AvailableProviders => _providers.Keys;

    public IEnumerable<IAiProvider> AllProviders => _providers.Values;
}
