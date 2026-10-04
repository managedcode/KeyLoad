namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Observes three nonsecret identities from one actual opened physical store and local control.</summary>
/// <param name="NodeId">The canonical store's actual persisted node identity.</param>
/// <param name="Incarnation">The canonical store's actual persisted database incarnation.</param>
/// <param name="RuntimeId">The actual control's process-local configuration identity.</param>
/// <remarks>This value grants no read authority, full silo readiness or remote cache eligibility.</remarks>
public readonly record struct ZoneTreePointCacheOwnerIdentity(Guid NodeId, Guid Incarnation, Guid RuntimeId);
