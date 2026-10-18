using System.Globalization;
using System.Text;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Projects native Aspire and process data into a closed diagnostic vocabulary.</summary>
internal static class ProcessRestartDiagnostics
{
    private const string UnknownValue = "unavailable";
    private const string OtherValue = "other";
    private const int GeneratedResourceIdSuffixLength = 8;
    private const string SnapshotFormat = "restart snapshot sequence={0} stage={1} resourceId={2} type={3} state={4} health={5} exit={6} creation={7} start={8} stop={9}";
    private const string KillReceiptFormat = "verified pre-kill process id={0} startedAt={1} aspireStartedAt={2}";
    private const string FailureFormat = "restart failure stage={0} type={1}";
    private static readonly CompositeFormat SnapshotTemplate = CompositeFormat.Parse(SnapshotFormat);
    private static readonly CompositeFormat KillReceiptCompositeFormat = CompositeFormat.Parse(KillReceiptFormat);
    private static readonly CompositeFormat FailureCompositeFormat = CompositeFormat.Parse(FailureFormat);

    /// <summary>Formats only validated native identifiers and selected public Aspire snapshot fields.</summary>
    /// <param name="stage">The closed snapshot point.</param>
    /// <param name="sequence">The local observer sequence number.</param>
    /// <param name="resourceEvent">The public current-state event, when available.</param>
    /// <returns>A line with unknown values replaced by fixed markers.</returns>
    internal static string FormatResourceSnapshot(ProcessRestartSnapshotStage stage, int sequence,
        ResourceEvent? resourceEvent)
    {
        var snapshot = resourceEvent?.Snapshot;
        var resourceId = ValidatedResourceId(resourceEvent);
        var resourceType = resourceEvent?.Resource is ProjectResource ? "Project" : OtherValue;
        return string.Format(CultureInfo.InvariantCulture, SnapshotTemplate,
            sequence, SnapshotStageName(stage), resourceId, resourceType, SnapshotState(snapshot),
            SnapshotHealth(snapshot), snapshot?.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? UnknownValue,
            Timestamp(snapshot?.CreationTimeStamp), Timestamp(snapshot?.StartTimeStamp), Timestamp(snapshot?.StopTimeStamp));
    }

    /// <summary>Formats only the verified old process identity and its observed start times.</summary>
    /// <param name="before">The actual native process before the abrupt fault.</param>
    /// <returns>A bounded safe receipt line.</returns>
    internal static string FormatVerifiedKill(NativeProcessEvidence before)
        => string.Format(CultureInfo.InvariantCulture, KillReceiptCompositeFormat, before.ProcessId,
            Timestamp(before.StartedAtUtc), Timestamp(before.AspireStartedAtUtc));

    /// <summary>Formats a closed failure stage and exception category without exception text.</summary>
    /// <param name="stage">The closed restart failure stage.</param>
    /// <param name="failure">The original failure object.</param>
    /// <returns>A line containing only fixed enum/category names.</returns>
    internal static string FormatFailure(ProcessRestartStage stage, Exception failure) =>
        string.Format(CultureInfo.InvariantCulture, FailureCompositeFormat, StageName(stage), FailureType(failure));

    private static string SnapshotState(CustomResourceSnapshot? snapshot) => snapshot?.State?.Text switch
    {
        var state when state == KnownResourceStates.Waiting => "Waiting",
        var state when state == KnownResourceStates.Starting => "Starting",
        var state when state == KnownResourceStates.Running => "Running",
        var state when state == KnownResourceStates.Finished => "Finished",
        var state when state == KnownResourceStates.Exited => "Exited",
        var state when state == KnownResourceStates.FailedToStart => "FailedToStart",
        var state when state == KnownResourceStates.RuntimeUnhealthy => "RuntimeUnhealthy",
        var state when state == KnownResourceStates.Stopping => "Stopping",
        var state when state == KnownResourceStates.NotStarted => "NotStarted",
        var state when state == KnownResourceStates.Building => "Building",
        var state when state == KnownResourceStates.ValueMissing => "ValueMissing",
        var state when state == KnownResourceStates.Active => "Active",
        null => UnknownValue,
        _ => OtherValue
    };

    private static string SnapshotHealth(CustomResourceSnapshot? snapshot) => snapshot?.HealthStatus switch
    {
        HealthStatus.Healthy => "Healthy",
        HealthStatus.Degraded => "Degraded",
        HealthStatus.Unhealthy => "Unhealthy",
        null => UnknownValue,
        _ => OtherValue
    };

    private static string Timestamp(DateTime? value) => value is { Kind: DateTimeKind.Utc } timestamp
        ? timestamp.ToString("O", CultureInfo.InvariantCulture)
        : UnknownValue;

    private static string ValidatedResourceId(ResourceEvent? resourceEvent)
    {
        if (resourceEvent is null || !ClusterFixtureProtocol.IsNodeName(resourceEvent.Resource.Name))
        {
            return UnknownValue;
        }

        var resourceName = resourceEvent.Resource.Name;
        var resourceId = resourceEvent.ResourceId;
        if (resourceId == resourceName)
        {
            return resourceId;
        }

        var prefix = resourceName + "-";
        if (resourceId.StartsWith(prefix, StringComparison.Ordinal)
            && IsGeneratedSuffix(resourceId.AsSpan(prefix.Length)))
        {
            return resourceId;
        }

        return UnknownValue;
    }

    private static bool IsGeneratedSuffix(ReadOnlySpan<char> suffix)
    {
        if (suffix.Length != GeneratedResourceIdSuffixLength)
        {
            return false;
        }

        foreach (var character in suffix)
        {
            if (character is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'))
            {
                return false;
            }
        }

        return true;
    }

    private static string SnapshotStageName(ProcessRestartSnapshotStage stage) => stage switch
    {
        ProcessRestartSnapshotStage.BeforeStart => "BeforeStart",
        ProcessRestartSnapshotStage.AfterStartSucceeded => "AfterStartSucceeded",
        ProcessRestartSnapshotStage.AcceptedStartRetained => "AcceptedStartRetained",
        ProcessRestartSnapshotStage.Failure => "Failure",
        _ => OtherValue
    };

    private static string StageName(ProcessRestartStage stage) => stage switch
    {
        ProcessRestartStage.StartCommand => "StartCommand",
        ProcessRestartStage.HealthWait => "HealthWait",
        ProcessRestartStage.RuntimeInspection => "RuntimeInspection",
        ProcessRestartStage.IdentityValidation => "IdentityValidation",
        ProcessRestartStage.SourceReceipt => "SourceReceipt",
        _ => OtherValue
    };

    private static string FailureType(Exception failure) => failure switch
    {
        OperationCanceledException => "OperationCanceled",
        TimeoutException => "Timeout",
        InvalidOperationException => "InvalidOperation",
        IOException => "IO",
        _ => OtherValue
    };
}
