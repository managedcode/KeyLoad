using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Actual process HTTP observations; counters reset with the host process.</summary>
/// <param name="StartedAt">Process counter start, independent of persisted storage incarnation.</param>
/// <param name="ProcessInstance">Unique nonsecret lifetime identity for rate reset detection.</param>
/// <param name="CompletedRequests">Completed public API HTTP requests excluding dashboard polling.</param>
/// <param name="FailedRequests">Completed requests with an error status or aborted response.</param>
/// <param name="ElapsedMilliseconds">Sum of measured request elapsed milliseconds.</param>
public sealed record AdminHttpSnapshot(DateTimeOffset StartedAt, Guid ProcessInstance, long CompletedRequests,
    long FailedRequests, double ElapsedMilliseconds)
{
    /// <summary>Gets the newest-first bounded process-local failed request log; resets with the host process.</summary>
    public ImmutableArray<AdminHttpFailure> RecentFailures { get; init; } = [];
}

/// <summary>One failed public API request without raw paths, query strings, payloads, headers or credentials.</summary>
/// <param name="At">Completion time of the failed request.</param>
/// <param name="Method">Normalized HTTP method; unrecognized methods use a fixed marker.</param>
/// <param name="Route">Matched endpoint route template, or a fixed unmatched marker; never the raw request path.</param>
/// <param name="StatusCode">Final HTTP response status code.</param>
/// <param name="Aborted">Whether the caller aborted the request before completion.</param>
/// <param name="ElapsedMilliseconds">Measured request elapsed milliseconds.</param>
public sealed record AdminHttpFailure(DateTimeOffset At, string Method, string Route, int StatusCode, bool Aborted,
    double ElapsedMilliseconds);

/// <summary>One observed physical file; never exposes an absolute path or file contents.</summary>
/// <param name="Path">Path relative to the owning physical node directory.</param>
/// <param name="Category">Canonical, replica, backup or other physical file category.</param>
/// <param name="Bytes">Observed file length, not allocated disk blocks.</param>
public sealed record AdminFileInfo(string Path, string Category, long Bytes);

/// <summary>Bounded physical filesystem observation, independent of the database read cut.</summary>
/// <param name="CanonicalBytes">Observed canonical store file lengths, null when unavailable.</param>
/// <param name="ReplicaBytes">Observed replica journal file lengths, null when unavailable.</param>
/// <param name="BackupBytes">Observed backup file lengths, null when unavailable.</param>
/// <param name="TotalBytes">Observed node file lengths, null when unavailable.</param>
/// <param name="ObservedFiles">Number of files whose lengths were included.</param>
/// <param name="Complete">Whether all eligible files were observed within the bounds.</param>
/// <param name="Files">Finite display subset; its sum need not equal total observed bytes.</param>
/// <param name="Notice">Safe incomplete/unavailable reason without sensitive filesystem details.</param>
public sealed record AdminStorageSnapshot(long? CanonicalBytes, long? ReplicaBytes, long? BackupBytes,
    long? TotalBytes, int ObservedFiles, bool Complete, ImmutableArray<AdminFileInfo> Files, string? Notice);

/// <summary>Administrator-authorized observation of the actual executing node.</summary>
/// <param name="CapturedAt">Node capture time for process counter sampling.</param>
/// <param name="Node">Actual physical status; never implies all voters are healthy.</param>
/// <param name="Admission">Current node command and HTTP occupancy, not throughput.</param>
/// <param name="Storage">Bounded physical file observation.</param>
/// <param name="Http">Actual process HTTP observations.</param>
public sealed record AdminNodeSnapshot(DateTimeOffset CapturedAt, NodeStatus Node, NodeAdmissionStatus Admission,
    AdminStorageSnapshot Storage, AdminHttpSnapshot Http)
{
    /// <summary>Gets the configured voter identity of the executing node, comparable with the reported leader.</summary>
    public string? LocalVoter { get; init; }

    /// <summary>Gets the configured voter membership; membership never implies that another voter is healthy.</summary>
    public ImmutableArray<string> Voters { get; init; } = [];
}
