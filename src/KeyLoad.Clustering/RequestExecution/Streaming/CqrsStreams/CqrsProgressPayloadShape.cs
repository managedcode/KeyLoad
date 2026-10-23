namespace KeyLoad.Clustering;

/// <summary>Requires one closed operation payload before any mutable per-operation progress state advances.</summary>
internal static class CqrsProgressPayloadShape
{
    internal static void RequireSingle(GrainRequestProgress progress)
    {
        var count = (progress.AnnPhase is null ? 0 : 1)
            + (progress.TextPhase is null ? 0 : 1)
            + (progress.OnlineTextPhase is null ? 0 : 1);
        if (count != 1)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
