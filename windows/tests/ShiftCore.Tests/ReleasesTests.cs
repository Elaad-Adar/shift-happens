using ShiftCore;

namespace ShiftCore.Tests;

/// <summary>Same cases as the Swift <c>ReleasesTests</c>.</summary>
public class ReleasesTests
{
    [Fact]
    public void Parse()
    {
        Assert.Equal(new[] { 1, 2, 0 }, Releases.Parse("v1.2.0"));
        Assert.Equal(new[] { 1, 10 }, Releases.Parse("1.10"));
        Assert.Equal(new[] { 2, 0, 0 }, Releases.Parse("2.0.0-beta.1"));
        Assert.Null(Releases.Parse("latest"));
    }

    [Theory]
    [InlineData("v1.3.0", "1.2.0", true)]
    [InlineData("1.10.0", "1.9.9", true)]
    [InlineData("1.2.1", "1.2", true)]
    [InlineData("v1.2.0", "1.2.0", false)]
    [InlineData("1.2", "1.2.0", false)]
    [InlineData("1.1.9", "1.2.0", false)]
    [InlineData("junk", "1.2.0", false)]
    public void IsNewer(string candidate, string current, bool expected) =>
        Assert.Equal(expected, Releases.IsNewer(candidate, current));

    [Fact]
    public void DecodeLatest()
    {
        var latest = Releases.DecodeLatest("{\"tag_name\":\"v1.3.0\",\"html_url\":\"https://github.com/Elaad-Adar/shift-happens/releases/tag/v1.3.0\"}");
        Assert.Equal("1.3.0", latest?.Version);
        Assert.EndsWith("/releases/tag/v1.3.0", latest?.Url.ToString());
        Assert.Null(Releases.DecodeLatest("{\"message\":\"Not Found\"}"));
        Assert.Null(Releases.DecodeLatest("not json"));
    }

    [Fact]
    public void IssueUrl()
    {
        var url = Releases.IssueUrl(Releases.IssueKind.Bug, "1.3.0", "Windows 11 24H2").AbsoluteUri;
        Assert.StartsWith("https://github.com/Elaad-Adar/shift-happens/issues/new?", url);
        Assert.Contains("template=bug.yml", url);
        Assert.Contains("version=1.3.0", url);
        Assert.Contains("os=Windows%2011%2024H2", url);
        Assert.Contains("template=idea.yml", Releases.IssueUrl(Releases.IssueKind.Idea, "1", "x").AbsoluteUri);
    }
}
