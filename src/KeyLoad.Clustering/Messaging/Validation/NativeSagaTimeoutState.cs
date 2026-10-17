using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Clustering;

internal readonly record struct NativeSagaTimeoutState(
    bool IsEligible, SagaRecord? Record, PrincipalRecord? Creator)
{
    internal static NativeSagaTimeoutState Stale => new(false, null, null);
}
