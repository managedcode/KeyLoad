namespace KeyLoad;

/// <summary>Selects explicit construction, exact native restoration, or release of one admin generation.</summary>
public enum AnnMaintenanceMode
{
    /// <summary>Construct a new acceleration generation from the authorized canonical source.</summary>
    Build,
    /// <summary>Load existing native arrays and replay the retained canonical prefix.</summary>
    Restore,
    /// <summary>Fence a generation and release its persisted projection pin.</summary>
    Release
}

/// <summary>Addresses a bounded admin operation; this parent has no atomic group receipt.</summary>
/// <param name="CommandId">Stable parent identity used for distinct canonical child identities.</param>
/// <param name="Consumer">Exact persisted projection consumer and atomic partition.</param>
/// <param name="Collection">Canonical vector source collection.</param>
/// <param name="Field">Canonical vector source field.</param>
/// <param name="Space">Exact model, version, dimension and metric.</param>
/// <param name="IndexGeneration">Explicit positive generation of the active consumer.</param>
/// <param name="NodeId">Exact opaque node-local storage owner.</param>
/// <param name="Placement">Exact persisted physical shard placement witness.</param>
/// <param name="Mode">Explicit build, restore or release; restore never rebuilds instead of loading.</param>
[Orleans.GenerateSerializer, Orleans.Alias(AnnMaintenanceAliases.Request)]
public sealed record AnnMaintenanceRequest(
    [property: Orleans.Id(0)] Guid CommandId,
    [property: Orleans.Id(1)] ProjectionConsumerRef Consumer,
    [property: Orleans.Id(2)] string Collection,
    [property: Orleans.Id(3)] string Field,
    [property: Orleans.Id(4)] VectorSpace Space,
    [property: Orleans.Id(5)] long IndexGeneration,
    [property: Orleans.Id(6)] Guid NodeId,
    [property: Orleans.Id(7)] PhysicalShardRecord Placement,
    [property: Orleans.Id(8)] AnnMaintenanceMode Mode);

/// <summary>Names bounded maintenance phases without payload or credentials.</summary>
public enum AnnMaintenancePhase
{
    /// <summary>Admit and pin the canonical projection consumer.</summary>
    Configure,
    /// <summary>Retain one complete bounded canonical seed and release the raw read view.</summary>
    Capture,
    /// <summary>Load or construct exact native arrays.</summary>
    NativeIndex,
    /// <summary>Apply only the admitted signed outbox interval.</summary>
    Replay,
    /// <summary>Verify the exact canonical upper source cut.</summary>
    Verify,
    /// <summary>Flush and publish one immutable generation.</summary>
    Publish,
    /// <summary>Observe the actual canonical projection checkpoint receipt.</summary>
    Checkpoint,
    /// <summary>Fence and release an obsolete generation.</summary>
    Release,
    /// <summary>The bounded maintenance operation completed.</summary>
    Completed
}

internal static class AnnMaintenanceAliases
{
    internal const string Request = "keyload.search.ann-maintenance-request.v1";
    internal const string Cut = "keyload.search.ann-source-cut.v1";
    internal const string Result = "keyload.search.ann-maintenance-result.v1";
}
