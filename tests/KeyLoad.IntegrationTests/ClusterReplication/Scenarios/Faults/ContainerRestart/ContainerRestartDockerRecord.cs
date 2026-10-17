using System.Globalization;
using System.Text;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Parses and projects only selected native Docker inspection values.</summary>
internal static class ContainerRestartDockerRecord
{
    private const char InspectSeparator = '|';
    private const int InspectFieldCount = 7;
    private const string UnknownValue = "unavailable";
    private const string OtherValue = "other";
    private const string DockerUnavailableLine = "Docker restart inspection unavailable.";
    private const string DockerInspectFormat = "restart Docker id={0} state={1} exit={2} oomKilled={3} startedAt={4} finishedAt={5} hasError={6}";
    private static readonly CompositeFormat DockerInspectCompositeFormat = CompositeFormat.Parse(DockerInspectFormat);

    internal static string FormatInspection(string output)
    {
        if (!TryParse(output, out var inspection))
        {
            return DockerUnavailableLine;
        }

        return string.Format(CultureInfo.InvariantCulture, DockerInspectCompositeFormat, inspection.Id,
            inspection.State, inspection.ExitCode, inspection.OomKilled, inspection.StartedAt,
            inspection.FinishedAt, inspection.HasError);
    }

    internal static string CanonicalContainerId(string value) => IsValidContainerId(value) ? value : UnknownValue;

    internal static string ClosedState(string value) => TryState(value, out var state) ? state : OtherValue;

    internal static string ClosedTimestamp(string value) => TryTimestamp(value, out var timestamp) ? timestamp : UnknownValue;

    private static bool TryParse(string output, out DockerInspection inspection)
    {
        var fields = output.Trim().Split(InspectSeparator, InspectFieldCount, StringSplitOptions.None);
        if (fields.Length != InspectFieldCount || !IsValidContainerId(fields[0])
                                               || !TryState(fields[1], out var state)
                                               || !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var exitCode)
                                               || exitCode < 0
                                               || !bool.TryParse(fields[3], out var oomKilled)
                                               || !TryTimestamp(fields[4], out var startedAt)
                                               || !TryTimestamp(fields[5], out var finishedAt))
        {
            inspection = default;
            return false;
        }

        inspection = new(fields[0], state, exitCode, oomKilled, startedAt, finishedAt,
            fields[6].Length > 0);
        return true;
    }

    private static bool IsValidContainerId(string value) =>
        value.Length == ContainerRuntimeProtocol.FullContainerIdCharacters && value.All(Uri.IsHexDigit);

    private static bool TryState(string value, out string state)
    {
        state = value switch
        {
            "created" => "created",
            "restarting" => "restarting",
            "running" => "running",
            "removing" => "removing",
            "paused" => "paused",
            "exited" => "exited",
            "dead" => "dead",
            _ => OtherValue
        };
        return state != OtherValue;
    }

    private static bool TryTimestamp(string value, out string timestamp)
    {
        var hasExplicitUtcOffset = value.EndsWith('Z') || value.EndsWith("+00:00", StringComparison.Ordinal);
        if (hasExplicitUtcOffset
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            && parsed.Offset == TimeSpan.Zero)
        {
            timestamp = parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            return true;
        }

        timestamp = UnknownValue;
        return false;
    }

    private readonly record struct DockerInspection(string Id, string State, int ExitCode, bool OomKilled,
        string StartedAt, string FinishedAt, bool HasError);
}
