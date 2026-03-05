using System.Text.Json;

namespace Compass;

public class PersonalizationManager
{
    public static string GetPersonalizationSystemPrompt() => @"You are an expert UI/UX designer helping users personalize their Compass application.
When a user describes how they want their Compass to look, extract the specific visual changes they want and return a JSON object with ONLY the properties they specified.

Parse the user's natural language request and extract specific values. Only include properties the user explicitly mentions.
Be creative and interpret user intent from natural descriptions (e.g., 'make it cooler' -> blues and modern animations).

Return a valid JSON object with ONLY these possible properties (omit properties not mentioned):
{
  ""CompassBoxDefaultText"": ""string (placeholder text in the input box)"",
  ""PrimaryColor"": ""#RRGGBB (hex color, use dark for professional, bright for bold)"",
  ""AccentColor"": ""#RRGGBB (highlight/accent color)"",
  ""WindowWidth"": number,
  ""WindowHeight"": number,
  ""FontFamily"": ""string (e.g., 'Segoe UI, Arial, Courier New')"",
  ""FontSize"": number (e.g., 12, 14, 16, 18),
  ""AnimationsEnabled"": boolean,
  ""BorderColor"": ""#RRGGBB (border/outline color)"",
  ""BorderRadius"": number (e.g., 4, 8, 12, 16 for corner roundness)
}

Return ONLY the JSON object, no other text. If the request is invalid or not about personalization, return an empty JSON object: {}";

    public static PersonalizationProposal ParseResponse(string jsonResponse)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<PersonalizationProposal>(jsonResponse, options) ?? new PersonalizationProposal();
        }
        catch
        {
            return new PersonalizationProposal();
        }
    }
}
