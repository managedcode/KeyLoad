
namespace KeyLoad.Orleans;

internal static class PhysicalCommandActorKey
{
    private const string Prefix = "owner-v1:";
    private const string IncarnationFormat = "N";
    private const char Separator = ':';
    private const int IncarnationCharacters = 32;

    internal static string Resolve(DecodedGrainRequest request, IPhysicalRequestPlacement? physical)
    {
        var logical = GrainPartitionResolver.Resolve(request);
        if (physical is null)
        { return logical; }
        if (physical.Owner.Incarnation == Guid.Empty || request.Envelope.Incarnation != physical.Owner.Incarnation)
        { throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest); }
        return string.Concat(Prefix, physical.Owner.Incarnation.ToString(IncarnationFormat), Separator, logical);
    }

    internal static void Validate(DecodedGrainRequest request, string actorKey, IPhysicalRequestPlacement? physical)
    {
        if (physical is not null)
        {
            var offset = Prefix.Length;
            if (!actorKey.StartsWith(Prefix, StringComparison.Ordinal)
                || actorKey.Length <= offset + IncarnationCharacters
                || actorKey[offset + IncarnationCharacters] != Separator
                || !Guid.TryParseExact(actorKey.AsSpan(offset, IncarnationCharacters), IncarnationFormat, out var incarnation)
                || incarnation != physical.Owner.Incarnation)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        }
        if (!string.Equals(Resolve(request, physical), actorKey, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
    }
}
