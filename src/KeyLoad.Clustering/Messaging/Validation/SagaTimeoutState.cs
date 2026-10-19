using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Clustering;

internal readonly record struct SagaTimeoutState(
    bool IsEligible, SagaRecord? Record, PrincipalRecord? Creator)
{
    internal static SagaTimeoutState Stale => new(false, null, null);
}
