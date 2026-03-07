using System.IO;
using System.Security.Cryptography;
using System.Text;
using Compass.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class CredentialService : ICredentialService
{
    private static readonly string CredentialPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Compass", "credentials");

    private readonly ILogger<CredentialService> _logger;

    public CredentialService(ILogger<CredentialService> logger)
    {
        _logger = logger;
    }

    public string? GetApiKey(string provider)
    {
        string filePath = GetFilePath(provider);
        if (!File.Exists(filePath)) return null;

        try
        {
            byte[] encrypted = File.ReadAllBytes(filePath);
            byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve API key for {Provider}", provider);
            return null;
        }
    }

    public void SetApiKey(string provider, string key)
    {
        try
        {
            Directory.CreateDirectory(CredentialPath);
            byte[] plaintext = Encoding.UTF8.GetBytes(key);
            byte[] encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(GetFilePath(provider), encrypted);
            _logger.LogInformation("API key stored securely for {Provider}", provider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store API key for {Provider}", provider);
        }
    }

    public void DeleteApiKey(string provider)
    {
        string filePath = GetFilePath(provider);
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
                _logger.LogInformation("API key deleted for {Provider}", provider);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete API key for {Provider}", provider);
            }
        }
    }

    private static string GetFilePath(string provider)
    {
        // Sanitize provider name for filename
        string safe = string.Join("_", provider.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(CredentialPath, $"{safe}.key");
    }
}
