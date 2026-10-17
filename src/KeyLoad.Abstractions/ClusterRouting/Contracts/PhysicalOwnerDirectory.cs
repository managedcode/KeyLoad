using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Records registered physical identities without granting remote operation authority.</summary>
/// <param name="Version">The independent directory format.</param>
/// <param name="Revision">The committed directory CAS revision.</param>
/// <param name="ControlOwner">The unchanged stable control owner.</param>
/// <param name="Owners">The bounded registered groups, including the control group.</param>
[Orleans.GenerateSerializer, Orleans.Alias(PhysicalOwnerDirectoryAliases.Directory)]
public sealed record PhysicalOwnerDirectoryV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] long Revision,
    [property: Orleans.Id(2)] PhysicalShardRecord ControlOwner,
    [property: Orleans.Id(3)] ImmutableArray<RegisteredPhysicalOwnerV1> Owners);

/// <summary>Records a configured replica group and its corresponding ordered server endpoints.</summary>
/// <param name="Owner">The confirmed physical identity.</param>
/// <param name="Endpoints">The ordered canonical server bases, with no credentials.</param>
[Orleans.GenerateSerializer, Orleans.Alias(PhysicalOwnerDirectoryAliases.Owner)]
public sealed record RegisteredPhysicalOwnerV1(
    [property: Orleans.Id(0)] PhysicalShardRecord Owner,
    [property: Orleans.Id(1)] ImmutableArray<string> Endpoints);
