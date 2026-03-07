using System.IO;
using Compass.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compass.Tests;

public class SnippetServiceTests
{
    private readonly SnippetService _service;

    public SnippetServiceTests()
    {
        _service = new SnippetService(NullLogger<SnippetService>.Instance);
        // Clean up any pre-existing test snippets
        foreach (var s in _service.GetAll().Where(s => s.Keyword.StartsWith("_test_")).ToList())
            _service.Delete(s.Keyword);
    }

    [Fact]
    public void Add_ThenGetAll_ContainsSnippet()
    {
        var keyword = "_test_add_" + Guid.NewGuid().ToString("N")[..8];
        _service.Add(new Snippet { Keyword = keyword, Title = "Test Add", Content = "Content" });

        var all = _service.GetAll();
        Assert.Contains(all, s => s.Keyword == keyword);

        // cleanup
        _service.Delete(keyword);
    }

    [Fact]
    public void Search_MatchesKeyword()
    {
        var keyword = "_test_srch_" + Guid.NewGuid().ToString("N")[..8];
        _service.Add(new Snippet { Keyword = keyword, Title = "Search Test", Content = "Some content" });

        var results = _service.Search(keyword);
        Assert.Single(results);
        Assert.Equal(keyword, results[0].Keyword);

        _service.Delete(keyword);
    }

    [Fact]
    public void Search_MatchesTitle()
    {
        var keyword = "_test_title_" + Guid.NewGuid().ToString("N")[..8];
        var title = "UniqueTitle_" + Guid.NewGuid().ToString("N")[..8];
        _service.Add(new Snippet { Keyword = keyword, Title = title, Content = "Content" });

        var results = _service.Search(title);
        Assert.Single(results);

        _service.Delete(keyword);
    }

    [Fact]
    public void Delete_RemovesSnippet()
    {
        var keyword = "_test_del_" + Guid.NewGuid().ToString("N")[..8];
        _service.Add(new Snippet { Keyword = keyword, Title = "Del Test", Content = "Content" });
        Assert.Contains(_service.GetAll(), s => s.Keyword == keyword);

        _service.Delete(keyword);
        Assert.DoesNotContain(_service.GetAll(), s => s.Keyword == keyword);
    }

    [Fact]
    public void RecordUsage_IncrementsUsageCount()
    {
        var keyword = "_test_usage_" + Guid.NewGuid().ToString("N")[..8];
        _service.Add(new Snippet { Keyword = keyword, Title = "Usage Test", Content = "Content" });

        _service.RecordUsage(keyword);
        _service.RecordUsage(keyword);

        var snippet = _service.GetAll().First(s => s.Keyword == keyword);
        Assert.Equal(2, snippet.UsageCount);

        _service.Delete(keyword);
    }
}
