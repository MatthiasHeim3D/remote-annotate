namespace RemoteAnnotate.Client.Services;

public interface IUpdateChecker
{
    /// <summary>
    /// Returns the newest published release that is newer than <paramref name="currentVersion"/>,
    /// or <see langword="null"/> when the client is up to date or the check could not be completed.
    /// Failures are never thrown: an update notice is a courtesy, not something to interrupt for.
    /// </summary>
    Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken = default);
}

/// <summary>A newer release. <see cref="ReleaseUrl"/> is built by the client, never taken from the response.</summary>
public sealed record UpdateInfo(string Version, Uri ReleaseUrl);
