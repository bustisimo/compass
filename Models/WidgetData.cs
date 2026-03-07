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
