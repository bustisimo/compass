namespace Compass;

public class WeatherData
{
    public double Temperature { get; set; }
    public string Condition { get; set; } = "";
    public string Location { get; set; } = "";
    public int WeatherCode { get; set; }
    public double WindSpeed { get; set; }
    public int Humidity { get; set; }
}

public class SystemStatsData
{
    public double CpuPercent { get; set; }
    public double RamUsedGB { get; set; }
    public double RamTotalGB { get; set; }
    public double DiskUsedGB { get; set; }
    public double DiskTotalGB { get; set; }
}

public class AlarmEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public TimeSpan Time { get; set; }
    public string Label { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public List<DayOfWeek> Days { get; set; } = new(); // empty = every day
}
