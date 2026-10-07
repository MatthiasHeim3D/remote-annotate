namespace RemoteAnnotate.Client.Services;

public interface IServerConnectionTester
{
    Task<ServerConnectionTestResult> TestAsync(
        string serverAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <see cref="TestAsync"/> and then presents <paramref name="passwordKey"/> to the relay
    /// the way a client connecting to it would, without opening a connection or touching the live
    /// ones. A relay that is reachable but turns the key away is reported as a failure, so a
    /// success means this address and this password work together.
    /// </summary>
    Task<ServerConnectionTestResult> TestAccessAsync(
        string serverAddress,
        string? passwordKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of a connection test. A successful result always carries the
/// <paramref name="ServerVersion"/> the relay advertised: identifying itself as a Remote Annotate
/// relay is part of passing the test, so a server that cannot is reported as a failure.
/// </summary>
public sealed record ServerConnectionTestResult(
    bool IsSuccessful,
    string Message,
    string? ServerVersion = null);
