using System.Net;
using System.Net.Http;
using System.Text;
using RemoteAnnotate.Client.Services;

namespace RemoteAnnotate.Client.Tests.Services;

public sealed class GitHubUpdateCheckerTests
{
    [Theory]
    [InlineData("1.2.0", "1.1.1", true)]
    [InlineData("1.10.0", "1.9.9", true)]
    [InlineData("1.1.1", "1.1.1", false)]
    [InlineData("1.1.0", "1.1.1", false)]
    [InlineData("1.1.1", "1.1.1-beta.2", true)]
    [InlineData("1.1.1-beta.2", "1.1.1", false)]
    [InlineData("1.1.1-beta.10", "1.1.1-beta.9", true)]
    [InlineData("1.1.1-rc.1", "1.1.1-beta.9", true)]
    public void Compare_OrdersVersionsAsVersionsNotStrings(string a, string b, bool aIsNewer)
    {
        Assert.True(ReleaseVersion.TryParse(a, out var left));
        Assert.True(ReleaseVersion.TryParse(b, out var right));

        Assert.Equal(aIsNewer, left.CompareTo(right) > 0);
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3+abc123", "1.2.3")]
    [InlineData("v1.2.3-beta.1+abc", "1.2.3-beta.1")]
    public void TryParse_AcceptsTagsAndBuildMetadata(string text, string expected)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        Assert.Equal(expected, version.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1.2")]
    [InlineData("v1.2.x")]
    [InlineData("v1.2.3-")]
    [InlineData("v1.2.3-be ta")]
    public void TryParse_RejectsAnythingElse(string text) =>
        Assert.False(ReleaseVersion.TryParse(text, out _));

    [Fact]
    public async Task CheckAsync_NewerReleaseIsReportedWithABuiltReleaseUrl()
    {
        var checker = CreateChecker(
            """[{"tag_name":"v1.2.0","draft":false,"prerelease":true,"html_url":"https://evil.example/x"},{"tag_name":"v1.1.1"}]""");

        var update = await checker.CheckAsync("1.1.1");

        Assert.NotNull(update);
        Assert.Equal("1.2.0", update.Version);
        Assert.Equal(
            "https://github.com/MatthiasHeim3D/remote-annotate/releases/tag/v1.2.0",
            update.ReleaseUrl.AbsoluteUri);
    }

    [Fact]
    public async Task CheckAsync_PicksTheHighestVersionRegardlessOfOrder()
    {
        var checker = CreateChecker(
            """[{"tag_name":"v1.1.0"},{"tag_name":"v1.3.0"},{"tag_name":"v1.2.0"}]""");

        var update = await checker.CheckAsync("1.1.1");

        Assert.Equal("1.3.0", update?.Version);
    }

    [Fact]
    public async Task CheckAsync_DraftsAreIgnored()
    {
        var checker = CreateChecker("""[{"tag_name":"v2.0.0","draft":true}]""");

        Assert.Null(await checker.CheckAsync("1.1.1"));
    }

    [Fact]
    public async Task CheckAsync_PreReleasesCount()
    {
        var checker = CreateChecker("""[{"tag_name":"v1.2.0","prerelease":true}]""");

        Assert.Equal("1.2.0", (await checker.CheckAsync("1.1.1"))?.Version);
    }

    [Theory]
    [InlineData("1.1.1")]
    [InlineData("1.5.0")]
    public async Task CheckAsync_SameOrNewerLocalBuildShowsNoUpdate(string current)
    {
        var checker = CreateChecker("""[{"tag_name":"v1.1.1"}]""");

        Assert.Null(await checker.CheckAsync(current));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"message":"rate limited"}""")]
    [InlineData("""[{"tag_name":42},{"tag_name":"nonsense"},"x"]""")]
    public async Task CheckAsync_UnusablePayloadIsSilent(string payload)
    {
        var checker = CreateChecker(payload);

        Assert.Null(await checker.CheckAsync("1.1.1"));
    }

    [Fact]
    public async Task CheckAsync_ErrorStatusIsSilent()
    {
        var checker = CreateChecker("""[{"tag_name":"v9.0.0"}]""", HttpStatusCode.Forbidden);

        Assert.Null(await checker.CheckAsync("1.1.1"));
    }

    [Fact]
    public async Task CheckAsync_NetworkFailureIsSilent()
    {
        var checker = new GitHubUpdateChecker(new HttpClient(new StubHandler(
            _ => throw new HttpRequestException("offline"))));

        Assert.Null(await checker.CheckAsync("1.1.1"));
    }

    [Fact]
    public async Task CheckAsync_UnparseableCurrentVersionMakesNoRequest()
    {
        var requested = false;
        var checker = new GitHubUpdateChecker(new HttpClient(new StubHandler(_ =>
        {
            requested = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));

        Assert.Null(await checker.CheckAsync("garbage"));
        Assert.False(requested);
    }

    [Fact]
    public async Task CheckAsync_SendsUserAgentToGitHubOnly()
    {
        HttpRequestMessage? seen = null;
        var checker = new GitHubUpdateChecker(new HttpClient(new StubHandler(request =>
        {
            seen = request;
            return Json("[]");
        })));

        await checker.CheckAsync("1.1.1");

        Assert.Equal("api.github.com", seen?.RequestUri?.Host);
        Assert.NotEmpty(seen!.Headers.UserAgent);
    }

    private static GitHubUpdateChecker CreateChecker(
        string payload,
        HttpStatusCode status = HttpStatusCode.OK) =>
        new(new HttpClient(new StubHandler(_ => Json(payload, status))));

    private static HttpResponseMessage Json(string payload, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
