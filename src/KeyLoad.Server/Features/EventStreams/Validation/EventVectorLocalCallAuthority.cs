using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorLocalCallAuthority
{
    internal static void Require(EventVectorRemoteCall originalCall,
        EventVectorRemoteCallValidation authority, OrleansNode node,
        CancellationToken originalCancellationToken)
    {
        authority.Require(originalCall, originalCancellationToken);
        var actual = node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable);
        if (!actual.TransportReady
            || !PhysicalOwnerEntryValidation.SameOwner(authority.LocalOwner.Owner, authority.ControlOwner.Owner)
            || actual.Incarnation != authority.LocalOwner.Owner.Incarnation
            || originalCall.CallerVoter != actual.VoterId || originalCall.CallerSiloAddress != actual.SiloAddress
            || !PhysicalOwnerEntryValidation.SameOwner(originalCall.Source, authority.LocalOwner.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Unavailable); }
    }
}
