using Compass.Services;

namespace Compass.Tests;

public class FuzzyMatcherTests
{
    [Fact]
    public void ExactPrefixMatch_ReturnsMaxScore()
    {
        double score = FuzzyMatcher.Score("Chrome", "Chrome");
        Assert.Equal(1.0, score);
    }

    [Fact]
    public void PrefixMatch_ReturnsHighScore()
    {
        double score = FuzzyMatcher.Score("Chrome", "Chr");
        Assert.Equal(1.0, score);
    }

    [Fact]
    public void WordBoundaryMatch_ReturnsHighScore()
    {
        double score = FuzzyMatcher.Score("Visual Studio Code", "code");
        Assert.Equal(0.9, score);
    }

    [Fact]
    public void SubstringMatch_ReturnsMediumScore()
    {
        double score = FuzzyMatcher.Score("Notepad++", "pad");
        Assert.Equal(0.75, score);
    }

    [Fact]
    public void SequentialCharMatch_ReturnsPositiveScore()
    {
        double score = FuzzyMatcher.Score("Visual Studio Code", "vsc");
        Assert.True(score > 0);
    }

    [Fact]
    public void NoMatch_ReturnsZero()
    {
        double score = FuzzyMatcher.Score("Chrome", "xyz");
        Assert.Equal(0, score);
    }

    [Fact]
    public void EmptyQuery_ReturnsZero()
    {
        Assert.Equal(0, FuzzyMatcher.Score("Chrome", ""));
    }

    [Fact]
    public void EmptyText_ReturnsZero()
    {
        Assert.Equal(0, FuzzyMatcher.Score("", "test"));
    }

    [Fact]
    public void CaseInsensitive_Matches()
    {
        double score = FuzzyMatcher.Score("chrome", "CHR");
        Assert.Equal(1.0, score);
    }

    [Fact]
    public void PartialCharMismatch_ReturnsZero()
    {
        double score = FuzzyMatcher.Score("abc", "abz");
        Assert.Equal(0, score);
    }
}
