using System.Text.RegularExpressions;
using Compass.Services.Interfaces;

namespace Compass.Services;

public enum QueryComplexity { Simple, Standard, Complex }

public class ModelRoutingService : IModelRoutingService
{
    private static readonly Regex SimplePattern = new(
        @"\b(what time|what's the time|cpu usage|battery|ram usage|disk space|memory|uptime|ip address|hostname|who am i|date|weather|volume|brightness|wifi|bluetooth|screenshot)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ComplexPattern = new(
        @"\b(explain|analyze|analyse|write a|write me|create a|generate|build|implement|design|step by step|in detail|compare|contrast|pros and cons|refactor|debug|optimize|review|summarize|essay|article|story|code|script|program|function|class|algorithm)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public QueryComplexity ClassifyQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return QueryComplexity.Standard;

        // Short queries (under 6 words) that match simple patterns
        int wordCount = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount <= 8 && SimplePattern.IsMatch(query))
            return QueryComplexity.Simple;

        if (ComplexPattern.IsMatch(query))
            return QueryComplexity.Complex;

        // Very short queries default to simple
        if (wordCount <= 3)
            return QueryComplexity.Simple;

        return QueryComplexity.Standard;
    }

    public string SelectModel(string query, AppSettings settings, bool hasImages = false)
    {
        if (!settings.SmartRoutingEnabled)
            return settings.SelectedModel;

        // Images require a vision-capable (power) model
        if (hasImages)
            return settings.PowerModel;

        var complexity = ClassifyQuery(query);
        return complexity switch
        {
            QueryComplexity.Simple => settings.FastModel,
            QueryComplexity.Complex => settings.PowerModel,
            _ => settings.SelectedModel
        };
    }
}
