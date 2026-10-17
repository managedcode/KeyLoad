using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Records a configured replica group and its corresponding ordered server endpoints.</summary>
/// <param name="Owner">The confirmed physical identity.</param>
/// <param name="Endpoints">The ordered canonical server bases, with no credentials.</param>
[Orleans.GenerateSerializer, Orleans.Alias(PhysicalOwnerDirectoryAliases.Owner)]
public sealed record RegisteredPhysicalOwnerV1(
    [property: Orleans.Id(0)] PhysicalShardRecord Owner,
    [property: Orleans.Id(1)] ImmutableArray<string> Endpoints);
