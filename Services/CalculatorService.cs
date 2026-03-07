using System.Data;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class CalculatorService
{
    private readonly ILogger<CalculatorService> _logger;
    private static readonly HttpClient _httpClient = new();
    private Dictionary<string, double>? _exchangeRates;
    private DateTime _ratesLastFetched;

    public CalculatorService(ILogger<CalculatorService> logger)
    {
        _logger = logger;
    }

    private static readonly Regex UnitConversionPattern = new(
        @"^([\d.]+)\s*([a-zA-Z°]+)\s+(?:to|in)\s+([a-zA-Z°]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CurrencyPattern = new(
        @"^([\d.]+)\s*([A-Z]{3})\s+(?:to|in)\s+([A-Z]{3})$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string? TryEvaluate(string input)
    {
        // Currency conversion
        var currencyMatch = CurrencyPattern.Match(input);
        if (currencyMatch.Success)
        {
            if (double.TryParse(currencyMatch.Groups[1].Value, out double amount))
            {
                string from = currencyMatch.Groups[2].Value.ToUpperInvariant();
                string to = currencyMatch.Groups[3].Value.ToUpperInvariant();
                var result = ConvertCurrency(amount, from, to);
                if (result.HasValue)
                    return $"{amount} {from} = {result.Value:F2} {to}";
            }
        }

        // Unit conversion
        var unitMatch = UnitConversionPattern.Match(input);
        if (unitMatch.Success)
        {
            if (double.TryParse(unitMatch.Groups[1].Value, out double value))
            {
                string fromUnit = unitMatch.Groups[2].Value.ToLowerInvariant();
                string toUnit = unitMatch.Groups[3].Value.ToLowerInvariant();
                var result = ConvertUnit(value, fromUnit, toUnit);
                if (result.HasValue)
                    return $"{value} {fromUnit} = {result.Value:G6} {toUnit}";
            }
        }

        // Math expression
        try
        {
            var mathResult = new DataTable().Compute(input, null);
            if (mathResult != null && mathResult != DBNull.Value)
            {
                double val = Convert.ToDouble(mathResult);
                if (!double.IsNaN(val) && !double.IsInfinity(val))
                    return $"= {val:G10}";
            }
        }
        catch (Exception) { /* Expected for non-math input */ }

        return null;
    }

    private double? ConvertUnit(double value, string from, string to)
    {
        // Length
        var lengthToMeters = new Dictionary<string, double>
        {
            ["mm"] = 0.001, ["cm"] = 0.01, ["m"] = 1, ["km"] = 1000,
            ["in"] = 0.0254, ["ft"] = 0.3048, ["yd"] = 0.9144, ["mi"] = 1609.344
        };

        if (lengthToMeters.ContainsKey(from) && lengthToMeters.ContainsKey(to))
            return value * lengthToMeters[from] / lengthToMeters[to];

        // Weight
        var weightToKg = new Dictionary<string, double>
        {
            ["mg"] = 0.000001, ["g"] = 0.001, ["kg"] = 1, ["t"] = 1000,
            ["oz"] = 0.028349523125, ["lb"] = 0.45359237, ["lbs"] = 0.45359237, ["st"] = 6.35029
        };

        if (weightToKg.ContainsKey(from) && weightToKg.ContainsKey(to))
            return value * weightToKg[from] / weightToKg[to];

        // Temperature
        if ((from == "c" || from == "°c") && (to == "f" || to == "°f"))
            return value * 9.0 / 5.0 + 32;
        if ((from == "f" || from == "°f") && (to == "c" || to == "°c"))
            return (value - 32) * 5.0 / 9.0;
        if ((from == "c" || from == "°c") && (to == "k"))
            return value + 273.15;
        if ((from == "k") && (to == "c" || to == "°c"))
            return value - 273.15;

        // Storage
        var storageToBytes = new Dictionary<string, double>
        {
            ["b"] = 1, ["kb"] = 1024, ["mb"] = 1048576, ["gb"] = 1073741824, ["tb"] = 1099511627776
        };

        if (storageToBytes.ContainsKey(from) && storageToBytes.ContainsKey(to))
            return value * storageToBytes[from] / storageToBytes[to];

        // Time
        var timeToSeconds = new Dictionary<string, double>
        {
            ["ms"] = 0.001, ["s"] = 1, ["sec"] = 1, ["min"] = 60,
            ["hr"] = 3600, ["h"] = 3600, ["day"] = 86400, ["week"] = 604800
        };

        if (timeToSeconds.ContainsKey(from) && timeToSeconds.ContainsKey(to))
            return value * timeToSeconds[from] / timeToSeconds[to];

        // Volume
        var volumeToLiters = new Dictionary<string, double>
        {
            ["ml"] = 0.001, ["l"] = 1, ["gal"] = 3.78541, ["qt"] = 0.946353,
            ["pt"] = 0.473176, ["cup"] = 0.236588, ["floz"] = 0.0295735
        };

        if (volumeToLiters.ContainsKey(from) && volumeToLiters.ContainsKey(to))
            return value * volumeToLiters[from] / volumeToLiters[to];

        return null;
    }

    private double? ConvertCurrency(double amount, string from, string to)
    {
        try
        {
            if (_exchangeRates == null || (DateTime.UtcNow - _ratesLastFetched).TotalHours > 24)
                FetchExchangeRates().GetAwaiter().GetResult();

            if (_exchangeRates == null) return null;

            if (_exchangeRates.TryGetValue(from, out double fromRate) &&
                _exchangeRates.TryGetValue(to, out double toRate))
            {
                return amount * (toRate / fromRate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Currency conversion failed");
        }
        return null;
    }

    private async Task FetchExchangeRates()
    {
        try
        {
            // Using a free exchange rate API
            var response = await _httpClient.GetStringAsync("https://open.er-api.com/v6/latest/USD");
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("rates", out var rates))
            {
                _exchangeRates = new Dictionary<string, double>();
                foreach (var prop in rates.EnumerateObject())
                {
                    if (prop.Value.TryGetDouble(out double rate))
                        _exchangeRates[prop.Name] = rate;
                }
                _ratesLastFetched = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch exchange rates");
        }
    }
}
