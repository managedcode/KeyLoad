using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorAdmittedCall
{
    private EventVectorAdmittedCall(EventVectorMacCheckedCall original) => Original = original;
    internal EventVectorMacCheckedCall Original { get; }

    internal static async Task<EventVectorAdmittedCall> AdmitAsync(EventVectorMacCheckedCall original,
        EventVectorRemoteCallValidation authority, ReplicaMembershipAuthorityAddressPins pins,
        ReplicaMembershipAuthorityReplayCache replay, CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(pins);
        ArgumentNullException.ThrowIfNull(replay);
        authority.Require(original.Call, originalCancellationToken);
        var address = SiloAddress.FromParsableString(original.Call.CallerSiloAddress);
        if (address.Endpoint.Port != MembershipAuthoritySettingsProtocol.NativeSiloPort)
        { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
        var voter = authority.ControlOwner.Owner.VoterIds.IndexOf(original.Call.CallerVoter);
        await pins.PinCallerAsync(voter, address.Endpoint.Address, originalCancellationToken).ConfigureAwait(false);
        originalCancellationToken.ThrowIfCancellationRequested();
        if (!replay.TryUse(original.Call.Nonce))
        { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
        authority.Require(original.Call, originalCancellationToken);
        return new(original);
    }

    internal static EventVectorAdmittedCall AdmitLocal(EventVectorMacCheckedCall original,
        EventVectorRemoteCallValidation authority, OrleansNode node,
        ReplicaMembershipAuthorityReplayCache replay, CancellationToken originalCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(replay);
        authority.Require(original.Call, originalCancellationToken);
        EventVectorLocalCallAuthority.Require(original.Call, authority, node, originalCancellationToken);
        if (!replay.TryUse(original.Call.Nonce))
        { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
        return new(original);
    }

}
