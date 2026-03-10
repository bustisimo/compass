using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Compass.Services;

public class WeatherService
{
    private static readonly HttpClient _httpClient = new();
    private readonly ILogger<WeatherService> _logger;

    private WeatherData? _cachedWeather;
    private DateTime _cacheTime = DateTime.MinValue;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30);

    public WeatherService(ILogger<WeatherService> logger)
    {
        _logger = logger;
    }

    public async Task<WeatherData?> GetWeatherAsync(double lat, double lon)
    {
        if (_cachedWeather != null && DateTime.UtcNow - _cacheTime < _cacheDuration)
            return _cachedWeather;

        try
        {
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code,wind_speed_10m,relative_humidity_2m";
            var json = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("current", out var current))
                return null;

            var weather = new WeatherData
            {
                Temperature = current.GetProperty("temperature_2m").GetDouble(),
                WeatherCode = current.GetProperty("weather_code").GetInt32(),
                WindSpeed = current.GetProperty("wind_speed_10m").GetDouble(),
                Humidity = current.GetProperty("relative_humidity_2m").GetInt32(),
                Condition = GetConditionFromCode(current.GetProperty("weather_code").GetInt32())
            };

            _cachedWeather = weather;
            _cacheTime = DateTime.UtcNow;
            return weather;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch weather data");
            return _cachedWeather; // return stale cache if available
        }
    }

    public async Task<(double lat, double lon, string name)?> GeocodeAsync(string cityName)
    {
        try
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(cityName)}&count=1";
            var json = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                return null;

            var first = results[0];
            return (
                first.GetProperty("latitude").GetDouble(),
                first.GetProperty("longitude").GetDouble(),
                first.GetProperty("name").GetString() ?? cityName
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Geocoding failed for {City}", cityName);
            return null;
        }
    }

    public async Task<(double lat, double lon, string name)?> GetLocationByIpAsync()
    {
        try
        {
            var json = await _httpClient.GetStringAsync("http://ip-api.com/json");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("status", out var status) && status.GetString() != "success")
                return null;

            return (
                root.GetProperty("lat").GetDouble(),
                root.GetProperty("lon").GetDouble(),
                root.GetProperty("city").GetString() ?? "Unknown"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IP geolocation failed");
            return null;
        }
    }

    public void ClearCache()
    {
        _cachedWeather = null;
        _cacheTime = DateTime.MinValue;
    }

    private static string GetConditionFromCode(int code)
    {
        return code switch
        {
            0 => "Clear sky",
            1 => "Mainly clear",
            2 => "Partly cloudy",
            3 => "Overcast",
            45 or 48 => "Foggy",
            51 or 53 or 55 => "Drizzle",
            61 or 63 or 65 => "Rain",
            66 or 67 => "Freezing rain",
            71 or 73 or 75 => "Snow",
            77 => "Snow grains",
            80 or 81 or 82 => "Rain showers",
            85 or 86 => "Snow showers",
            95 => "Thunderstorm",
            96 or 99 => "Thunderstorm with hail",
            _ => "Unknown"
        };
    }
}
