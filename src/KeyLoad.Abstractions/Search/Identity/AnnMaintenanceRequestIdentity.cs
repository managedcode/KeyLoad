namespace KeyLoad;

internal static class AnnMaintenanceRequestIdentity
{
    internal static bool Same(AnnMaintenanceRequest first, AnnMaintenanceRequest second)
    {
        _ = NativeSerialization.Measure(first);
        _ = NativeSerialization.Measure(second);
        return first.CommandId == second.CommandId && first.Consumer == second.Consumer
            && first.Collection == second.Collection && first.Field == second.Field
            && first.Space == second.Space && first.IndexGeneration == second.IndexGeneration
            && first.NodeId == second.NodeId && first.Mode == second.Mode
            && first.MaintenanceProfile == second.MaintenanceProfile
            && first.Placement.PhysicalShardId == second.Placement.PhysicalShardId
            && first.Placement.Incarnation == second.Placement.Incarnation;
    }
}
