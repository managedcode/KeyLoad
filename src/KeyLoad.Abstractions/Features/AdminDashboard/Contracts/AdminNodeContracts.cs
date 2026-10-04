using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Actual process HTTP observations; counters reset with the host process.</summary>
/// <param name="StartedAt">Process counter start, independent of persisted storage incarnation.</param>
/// <param name="ProcessInstance">Unique nonsecret lifetime identity for rate reset detection.</param>
/// <param name="CompletedRequests">Completed public API HTTP requests excluding dashboard polling.</param>
/// <param name="FailedRequests">Completed requests with an error status or aborted response.</param>
/// <param name="ElapsedMilliseconds">Sum of measured request elapsed milliseconds.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminHttpSnapshot)]
public sealed record AdminHttpSnapshot([property: Orleans.Id(0)] DateTimeOffset StartedAt, [property: Orleans.Id(1)] Guid ProcessInstance, [property: Orleans.Id(2)] long CompletedRequests,
    [property: Orleans.Id(3)] long FailedRequests, [property: Orleans.Id(4)] double ElapsedMilliseconds)
{
    /// <summary>Gets the newest-first bounded process-local failed request log; resets with the host process.</summary>
    [Orleans.Id(5)]
    public ImmutableArray<AdminHttpFailure> RecentFailures { get; init; } = [];
}

/// <summary>One failed public API request without raw paths, query strings, payloads, headers or credentials.</summary>
/// <param name="At">Completion time of the failed request.</param>
/// <param name="Method">Normalized HTTP method; unrecognized methods use a fixed marker.</param>
/// <param name="Route">Matched endpoint route template, or a fixed unmatched marker; never the raw request path.</param>
/// <param name="StatusCode">Final HTTP response status code.</param>
/// <param name="Aborted">Whether the caller aborted the request before completion.</param>
/// <param name="ElapsedMilliseconds">Measured request elapsed milliseconds.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminHttpFailure)]
public sealed record AdminHttpFailure([property: Orleans.Id(0)] DateTimeOffset At, [property: Orleans.Id(1)] string Method, [property: Orleans.Id(2)] string Route, [property: Orleans.Id(3)] int StatusCode, [property: Orleans.Id(4)] bool Aborted,
    [property: Orleans.Id(5)] double ElapsedMilliseconds);

/// <summary>One observed physical file; never exposes an absolute path or file contents.</summary>
/// <param name="Path">Path relative to the owning physical node directory.</param>
/// <param name="Category">Canonical, replica, backup or other physical file category.</param>
/// <param name="Bytes">Observed file length, not allocated disk blocks.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminFileInfo)]
public sealed record AdminFileInfo([property: Orleans.Id(0)] string Path, [property: Orleans.Id(1)] string Category, [property: Orleans.Id(2)] long Bytes);

/// <summary>Bounded physical filesystem observation, independent of the database read cut.</summary>
/// <param name="CanonicalBytes">Observed canonical store file lengths, null when unavailable.</param>
/// <param name="ReplicaBytes">Observed replica journal file lengths, null when unavailable.</param>
/// <param name="BackupBytes">Observed backup file lengths, null when unavailable.</param>
/// <param name="TotalBytes">Observed node file lengths, null when unavailable.</param>
/// <param name="ObservedFiles">Number of files whose lengths were included.</param>
/// <param name="Complete">Whether all eligible files were observed within the bounds.</param>
/// <param name="Files">Finite display subset; its sum need not equal total observed bytes.</param>
/// <param name="Notice">Safe incomplete/unavailable reason without sensitive filesystem details.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminStorageSnapshot)]
public sealed record AdminStorageSnapshot([property: Orleans.Id(0)] long? CanonicalBytes, [property: Orleans.Id(1)] long? ReplicaBytes, [property: Orleans.Id(2)] long? BackupBytes,
    [property: Orleans.Id(3)] long? TotalBytes, [property: Orleans.Id(4)] int ObservedFiles, [property: Orleans.Id(5)] bool Complete, [property: Orleans.Id(6)] ImmutableArray<AdminFileInfo> Files, [property: Orleans.Id(7)] string? Notice);

/// <summary>Administrator-authorized observation of the actual executing node.</summary>
/// <param name="CapturedAt">Node capture time for process counter sampling.</param>
/// <param name="Node">Actual physical status; never implies all voters are healthy.</param>
/// <param name="Admission">Current node command and HTTP occupancy, not throughput.</param>
/// <param name="Storage">Bounded physical file observation.</param>
/// <param name="Http">Actual process HTTP observations.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminNodeSnapshot)]
public sealed record AdminNodeSnapshot([property: Orleans.Id(0)] DateTimeOffset CapturedAt, [property: Orleans.Id(1)] NodeStatus Node, [property: Orleans.Id(2)] NodeAdmissionStatus Admission,
    [property: Orleans.Id(3)] AdminStorageSnapshot Storage, [property: Orleans.Id(4)] AdminHttpSnapshot Http)
{
    /// <summary>Gets the configured voter identity of the executing node, comparable with the reported leader.</summary>
    [Orleans.Id(5)]
    public string? LocalVoter { get; init; }

    /// <summary>Gets the configured voter membership; membership never implies that another voter is healthy.</summary>
    [Orleans.Id(6)]
    public ImmutableArray<string> Voters { get; init; } = [];
}
