using System.Net.Http;
using System.Text.Json;

namespace RemoteAnnotate.Client.Services;

/// <summary>
/// Asks the public GitHub releases API whether a newer client exists. It only reads release
/// metadata: nothing is downloaded or installed, and the link it hands back is built from the
/// tag, so arbitrary response content never becomes a URL the user is sent to.
/// </summary>
/// <remarks>
/// The list endpoint is used instead of <c>/releases/latest</c> because that one skips
/// pre-releases and the client releases are currently published as pre-releases. Drafts are
/// ignored; pre-releases count.
/// </remarks>
public sealed class GitHubUpdateChecker : IUpdateChecker
{
    private const string ReleasesUrl =
        "https://api.github.com/repos/MatthiasHeim3D/remote-annotate/releases?per_page=30";
    private const string ReleasePageUrlPrefix =
        "https://github.com/MatthiasHeim3D/remote-annotate/releases/tag/";
    private const int MaximumResponseBytes = 512 * 1024;
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    private static readonly HttpClient SharedHttpClient = new(
        new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly HttpClient httpClient;

    public GitHubUpdateChecker()
        : this(SharedHttpClient)
    {
    }

    internal GitHubUpdateChecker(HttpClient httpClient) => this.httpClient = httpClient;

    public async Task<UpdateInfo?> CheckAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        if (!ReleaseVersion.TryParse(currentVersion, out var current))
        {
            return null;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CheckTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            request.Headers.UserAgent.ParseAdd($"RemoteAnnotate-Client/{current.Text}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await ReadBoundedAsync(response, timeout.Token).ConfigureAwait(false);
            return payload is null ? null : FindNewer(payload, current);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or OperationCanceledException
                or JsonException
                or InvalidOperationException
                or IOException)
        {
            return null;
        }
    }

    internal static UpdateInfo? FindNewer(byte[] payload, ReleaseVersion current)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        ReleaseVersion? best = null;
        string? bestTag = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object
                || (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                || !release.TryGetProperty("tag_name", out var tagElement)
                || tagElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var tag = tagElement.GetString();
            if (!ReleaseVersion.TryParse(tag, out var version)
                || (best is not null && version.CompareTo(best) <= 0))
            {
                continue;
            }

            best = version;
            bestTag = tag;
        }

        if (best is null || best.CompareTo(current) <= 0)
        {
            return null;
        }

        return new UpdateInfo(best.Text, new Uri(ReleasePageUrlPrefix + Uri.EscapeDataString(bestTag!)));
    }

    private static async Task<byte[]?> ReadBoundedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
        {
            return null;
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaximumResponseBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
