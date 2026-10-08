namespace KeyLoad;

/// <summary>Reports only the actual durable phase of a controlled partition movement.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(PartitionMoveAliases.Phase)]
public enum PartitionMovePhase
{
    /// <summary>No durable movement phase has been established.</summary>
    None = 0,
    /// <summary>The immutable control intent is durable.</summary>
    Prepared = 1,
    /// <summary>Original source admission is joined and its native fence is durable.</summary>
    Fenced = 2,
    /// <summary>The complete bounded checksummed source image is retained.</summary>
    Captured = 3,
    /// <summary>Original native destination page installation is in progress.</summary>
    Transferring = 4,
    /// <summary>The complete target image and quorum receipt are durable.</summary>
    Installed = 5,
    /// <summary>Control authority published the strictly newer destination placement.</summary>
    Published = 6,
    /// <summary>Original source cleanup completed after verified destination readiness.</summary>
    Retired = 7,
    /// <summary>Unpublished original staging is joined and source authority remains intact.</summary>
    Aborted = 8,
    /// <summary>New image admission is closed while actual original owners and staging are joined.</summary>
    Aborting = 9
}
