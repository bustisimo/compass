using System.Text.Json;

namespace Compass;

public class PersonalizationManager
{
    public static string GetPersonalizationSystemPrompt() => @"You are a creative visual designer for the Compass desktop app.
Transform the user's vision into a detailed visual specification.

Be bold and creative. Interpret vague requests imaginatively:
- ""make it cyberpunk"" → dark primary, neon pink/cyan accents, linear gradient
- ""flowing rainbow"" → colorful linear gradient with multiple colors
- ""minimal zen"" → matte white, no animations, thin border, large radius
- ""hacker mode"" → black bg, green accent, monospace font, no radius
- ""ocean vibes"" → deep blue to teal gradient, rounded corners

Available properties (JSON, omit any not relevant):
{
  ""PrimaryColor"": ""#RRGGBB"",
  ""AccentColor"": ""#RRGGBB"",
  ""SecondaryColor"": ""#RRGGBB (surface/card color override)"",
  ""TextColor"": ""#RRGGBB"",
  ""BorderColor"": ""#RRGGBB"",

  ""BackgroundType"": ""Solid | LinearGradient | RadialGradient"",
  ""GradientStartColor"": ""#RRGGBB"",
  ""GradientEndColor"": ""#RRGGBB"",
  ""GradientAngle"": 0-360,

  ""BorderThickness"": 0 to 4,
  ""BorderRadius"": 0 to 40,

  ""FontFamily"": ""font name"",
  ""FontSize"": 10 to 24,
  ""WindowWidth"": 500 to 1100,
  ""CompassBoxDefaultText"": ""placeholder text"",
  ""AnimationsEnabled"": true/false,
  ""CompactMode"": true/false
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
