namespace Compass.Services.Interfaces;

public interface IWidgetService
{
    string WidgetsPath { get; }
    void EnsureWidgetsFolderExists();
    List<CompassWidget> LoadCustomWidgets();
    void SaveWidget(CompassWidget widget);
    void DeleteWidget(string widgetId);
    List<CompassWidget> GetBuiltInWidgets();
    List<CompassWidget> GetAllWidgets();
}
