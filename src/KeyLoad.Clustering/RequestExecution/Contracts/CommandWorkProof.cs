using System.Collections.Immutable;

namespace KeyLoad.Clustering;

/// <summary>Carries the verified command owner's actual bounded dependencies to its original outer producer.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(GrainContracts.CommandWorkProofAlias)]
internal sealed record CommandWorkProof(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid RequestId,
    [property: Orleans.Id(2)] Guid CommandId,
    [property: Orleans.Id(3)] string PrincipalId,
    [property: Orleans.Id(4)] OperationKind Kind,
    [property: Orleans.Id(5)] ImmutableArray<ResourceLifetime> Lifetimes,
    [property: Orleans.Id(6)] long PolicyEpoch)
{

    internal const int CurrentVersion = 2;
    internal const long MinimumPolicyEpoch = 0;
}
