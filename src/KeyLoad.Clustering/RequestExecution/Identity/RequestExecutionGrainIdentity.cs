namespace KeyLoad.Clustering;

/// <summary>Canonical addressing of an admitted operation, independent of its stable command identity.</summary>
internal static class RequestExecutionGrainIdentity
{
    internal static Guid For(Guid requestId)
        => requestId != Guid.Empty ? requestId
            : throw new ArgumentException(GrainRoutingProtocol.InvalidRequest, nameof(requestId));
}
