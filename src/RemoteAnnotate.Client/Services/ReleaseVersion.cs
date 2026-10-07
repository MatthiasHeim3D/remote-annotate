using System.Globalization;

namespace RemoteAnnotate.Client.Services;

/// <summary>
/// A semantic version as used in release tags (<c>v1.2.3</c>, <c>v1.2.3-beta.1</c>). Build
/// metadata after <c>+</c> is ignored, as semantic versioning prescribes.
/// </summary>
internal sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
    private readonly Version core;
    private readonly string[] preRelease;

    private ReleaseVersion(Version core, string[] preRelease)
    {
        this.core = core;
        this.preRelease = preRelease;
    }

    public string Text =>
        preRelease.Length == 0
            ? core.ToString(3)
            : $"{core.ToString(3)}-{string.Join('.', preRelease)}";

    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 64)
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            value = value[..plus];
        }

        var dash = value.IndexOf('-', StringComparison.Ordinal);
        var corePart = dash >= 0 ? value[..dash] : value;
        var preReleasePart = dash >= 0 ? value[(dash + 1)..] : string.Empty;

        var pieces = corePart.Split('.');
        if (pieces.Length != 3
            || pieces.Any(piece => piece.Length == 0 || !piece.All(char.IsAsciiDigit))
            || !Version.TryParse(corePart, out var parsedCore))
        {
            return false;
        }

        string[] identifiers = [];
        if (dash >= 0)
        {
            identifiers = preReleasePart.Split('.');
            if (identifiers.Any(identifier => identifier.Length == 0
                || !identifier.All(char.IsAsciiLetterOrDigit)))
            {
                return false;
            }
        }

        version = new ReleaseVersion(parsedCore, identifiers);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byCore = core.CompareTo(other.core);
        if (byCore != 0)
        {
            return byCore;
        }

        // A release outranks any pre-release of the same core version.
        if (preRelease.Length == 0 || other.preRelease.Length == 0)
        {
            return other.preRelease.Length.CompareTo(preRelease.Length);
        }

        for (var index = 0; index < Math.Min(preRelease.Length, other.preRelease.Length); index++)
        {
            var left = preRelease[index];
            var right = other.preRelease[index];
            var leftNumeric = ulong.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = ulong.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
            var result = (leftNumeric, rightNumeric) switch
            {
                (true, true) => leftNumber.CompareTo(rightNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(left, right),
            };
            if (result != 0)
            {
                return result;
            }
        }

        return preRelease.Length.CompareTo(other.preRelease.Length);
    }
}
