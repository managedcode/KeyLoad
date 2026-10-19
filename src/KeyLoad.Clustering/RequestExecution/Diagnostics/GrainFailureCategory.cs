namespace KeyLoad.Clustering;

/// <summary>Closed categories emitted by internal rejected-request diagnostics.</summary>
internal enum GrainFailureCategory
{
    /// <summary>A domain exception with a typed KeyLoad error code.</summary>
    Domain,
    /// <summary>A malformed or incompatible JSON payload.</summary>
    Json,
    /// <summary>An invalid operation argument.</summary>
    Argument,
    /// <summary>A cancelled operation.</summary>
    Cancellation,
    /// <summary>An unexpected failure without a public exception classification.</summary>
    Unexpected
}
