namespace Compass.Services.Interfaces;

public interface ICredentialService
{
    string? GetApiKey(string provider);
    void SetApiKey(string provider, string key);
    void DeleteApiKey(string provider);
}
