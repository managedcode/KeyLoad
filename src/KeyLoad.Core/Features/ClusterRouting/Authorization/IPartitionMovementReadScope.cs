namespace KeyLoad.Core.Features.ClusterRouting.Authorization;

/// <summary>Retains an absence proof only while its owning canonical read cut remains unchanged.</summary>
internal interface IPartitionMovementReadScope
{
    bool MovementAuthorityAbsent { get; set; }
    bool CanReuseCommittedMovementAbsence { get; }
    bool CanPublishCommittedMovementAbsence { get; }
}
