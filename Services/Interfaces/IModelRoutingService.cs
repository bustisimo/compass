namespace Compass.Services.Interfaces;

public interface IModelRoutingService
{
    QueryComplexity ClassifyQuery(string query);
    string SelectModel(string query, AppSettings settings, bool hasImages = false);
}
