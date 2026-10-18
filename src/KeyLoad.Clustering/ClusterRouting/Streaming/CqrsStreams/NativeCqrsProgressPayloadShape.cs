namespace KeyLoad.Clustering;

/// <summary>Requires one closed operation payload before any mutable per-operation progress state advances.</summary>
internal static class NativeCqrsProgressPayloadShape
{
    private const int EmptyPayloads = 0;
    private const int SinglePayload = 1;

    internal static void RequireSingle(GrainRequestProgress progress)
    {
        var count = (progress.AnnPhase is null ? EmptyPayloads : SinglePayload)
            + (progress.TextPhase is null ? EmptyPayloads : SinglePayload)
            + (progress.OnlineTextPhase is null ? EmptyPayloads : SinglePayload);
        if (count != SinglePayload)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
