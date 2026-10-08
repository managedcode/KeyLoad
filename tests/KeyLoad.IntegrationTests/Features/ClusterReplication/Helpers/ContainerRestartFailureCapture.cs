using System.Globalization;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Closed failure points recorded for an unchanged native restart attempt.</summary>
internal enum ContainerRestartStage
{
    StartCommand,
    HealthWait,
    RuntimeInspection,
    IdentityValidation,
    SourceReceipt
}

/// <summary>Closed sample points around the original Aspire start operation.</summary>
internal enum ContainerRestartSampleStage
{
    BeforeStart,
    AfterStartSucceeded,
    AcceptedStartRetained,
    Failure
}

/// <summary>Samples safe Aspire state and actual Docker identity after a restart failure.</summary>
internal sealed class ContainerRestartFailureCapture
{
    private const string StartSucceededLine = "Aspire Start command succeeded.";
    private const string StartRetainedLine = "Original accepted Aspire Start retained; no new Start command issued.";

    private readonly DistributedApplication app;
    private readonly ContainerRuntimeKillReceipt receipt;
    private readonly string repositoryRoot;
    private readonly ClusterFailureReceipts failureReceipts;
    private readonly List<string> lines = new();
    private int sampleSequence;

    /// <summary>Captures the pre-start public Aspire snapshot and verified pre-kill identity.</summary>
    /// <param name="app">The actual Aspire application for this RF3 fixture.</param>
    /// <param name="receipt">The verified SIGKILL receipt retained by the fixture.</param>
    /// <param name="repositoryRoot">The checkout root for qualification artifacts.</param>
    /// <param name="failureReceipts">The fixture-owned restart receipt store.</param>
    internal ContainerRestartFailureCapture(DistributedApplication app, ContainerRuntimeKillReceipt receipt,
        string repositoryRoot, ClusterFailureReceipts failureReceipts)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(failureReceipts);
        this.app = app;
        this.receipt = receipt;
        this.repositoryRoot = repositoryRoot;
        this.failureReceipts = failureReceipts;
        AddSafeSample(ContainerRestartSampleStage.BeforeStart);
        lines.Add(ContainerRestartDiagnostics.FormatVerifiedKill(receipt.Before));
    }

    /// <summary>Samples the public Aspire state after native Start succeeds without changing its result.</summary>
    internal void StartSucceeded()
    {
        lines.Add(StartSucceededLine);
        AddSafeSample(ContainerRestartSampleStage.AfterStartSucceeded);
    }

    /// <summary>Records actual retained acceptance without manufacturing a new Start result.</summary>
    internal void StartRetained()
    {
        lines.Add(StartRetainedLine);
        AddSafeSample(ContainerRestartSampleStage.AcceptedStartRetained);
    }

    /// <summary>Captures the failure snapshot and one bounded read-only Docker inspection.</summary>
    /// <param name="stage">The closed restart stage that failed.</param>
    /// <param name="failure">The original failure; only its closed type category is retained.</param>
    internal async Task SaveAsync(ContainerRestartStage stage, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        AddSafeSample(ContainerRestartSampleStage.Failure);
        lines.Add(ContainerRestartDiagnostics.FormatFailure(stage, failure));
        lines.Add(await ContainerRestartDockerInspection.ReadAsync(receipt.ContainerName).ConfigureAwait(false));

        var output = Path.Combine(repositoryRoot, ClusterFixtureProtocol.ArtifactDirectory,
            ClusterFixtureProtocol.QualificationDirectory);
        Directory.CreateDirectory(output);
        failureReceipts.Save(output, BoundedDiagnosticLog.Bound(lines));
    }

    private void AddSafeSample(ContainerRestartSampleStage stage)
    {
        try
        {
            app.ResourceNotifications.TryGetCurrentState(receipt.ResourceName, out var current);
            lines.Add(ContainerRestartDiagnostics.FormatResourceSample(stage, ++sampleSequence, current));
        }
        catch (Exception error) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(error))
        {
            lines.Add(ContainerRestartDiagnostics.FormatResourceSample(stage, ++sampleSequence, null));
        }
    }
}

/// <summary>Projects native Aspire and Docker data into a closed diagnostic vocabulary.</summary>
internal static class ContainerRestartDiagnostics
{
    private const string UnknownValue = "unavailable";
    private const string OtherValue = "other";
    private const int GeneratedResourceIdSuffixLength = 8;
    private const string SampleFormat = "restart sample sequence={0} stage={1} resourceId={2} type={3} state={4} health={5} exit={6} creation={7} start={8} stop={9}";
    private const string KillReceiptFormat = "verified pre-kill container id={0} state={1} startedAt={2}";
    private const string FailureFormat = "restart failure stage={0} type={1}";
    private static readonly CompositeFormat SampleCompositeFormat = CompositeFormat.Parse(SampleFormat);
    private static readonly CompositeFormat KillReceiptCompositeFormat = CompositeFormat.Parse(KillReceiptFormat);
    private static readonly CompositeFormat FailureCompositeFormat = CompositeFormat.Parse(FailureFormat);

    /// <summary>Formats only validated native identifiers and selected public Aspire snapshot fields.</summary>
    /// <param name="stage">The closed sample point.</param>
    /// <param name="sequence">The local observer sequence number.</param>
    /// <param name="resourceEvent">The public current-state event, when available.</param>
    /// <returns>A line with unknown values replaced by fixed markers.</returns>
    internal static string FormatResourceSample(ContainerRestartSampleStage stage, int sequence,
        ResourceEvent? resourceEvent)
    {
        var snapshot = resourceEvent?.Snapshot;
        var resourceId = ValidatedResourceId(resourceEvent);
        var resourceType = resourceEvent?.Resource is ContainerResource ? "Container" : OtherValue;
        return string.Format(CultureInfo.InvariantCulture, SampleCompositeFormat,
            sequence, SampleStageName(stage), resourceId, resourceType, SnapshotState(snapshot),
            SnapshotHealth(snapshot), snapshot?.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? UnknownValue,
            Timestamp(snapshot?.CreationTimeStamp), Timestamp(snapshot?.StartTimeStamp), Timestamp(snapshot?.StopTimeStamp));
    }

    /// <summary>Formats only the verified old container identity and its closed state/start time.</summary>
    /// <param name="before">The actual inspected container before SIGKILL.</param>
    /// <returns>A bounded safe receipt line.</returns>
    internal static string FormatVerifiedKill(ContainerRuntimeInspection before)
    {
        var id = ContainerRestartDockerInspection.CanonicalContainerId(before.Id);
        var state = ContainerRestartDockerInspection.ClosedState(before.State);
        return string.Format(CultureInfo.InvariantCulture, KillReceiptCompositeFormat, id, state,
            ContainerRestartDockerInspection.ClosedTimestamp(before.StartedAt));
    }

    /// <summary>Formats a closed failure stage and exception category without exception text.</summary>
    /// <param name="stage">The closed restart failure stage.</param>
    /// <param name="failure">The original failure object.</param>
    /// <returns>A line containing only fixed enum/category names.</returns>
    internal static string FormatFailure(ContainerRestartStage stage, Exception failure) =>
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

        return ContainerRestartDockerInspection.IsNativeHexIdentifier(resourceId) ? resourceId : UnknownValue;
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

    private static string SampleStageName(ContainerRestartSampleStage stage) => stage switch
    {
        ContainerRestartSampleStage.BeforeStart => "BeforeStart",
        ContainerRestartSampleStage.AfterStartSucceeded => "AfterStartSucceeded",
        ContainerRestartSampleStage.AcceptedStartRetained => "AcceptedStartRetained",
        ContainerRestartSampleStage.Failure => "Failure",
        _ => OtherValue
    };

    private static string StageName(ContainerRestartStage stage) => stage switch
    {
        ContainerRestartStage.StartCommand => "StartCommand",
        ContainerRestartStage.HealthWait => "HealthWait",
        ContainerRestartStage.RuntimeInspection => "RuntimeInspection",
        ContainerRestartStage.IdentityValidation => "IdentityValidation",
        ContainerRestartStage.SourceReceipt => "SourceReceipt",
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
