namespace KeyLoad.Orleans.Features.ResourceExecution;

[GenerateSerializer, Alias(CacheControlNames.PhysicalBinding), Immutable]
internal sealed record CachePhysicalBinding(
    [property: Id(0)] CacheVoterSlot Slot,
    [property: Id(1)] Guid NodeId,
    [property: Id(2)] Guid Incarnation,
    [property: Id(3)] string SiloAddress,
    [property: Id(4)] Guid RuntimeId,
    [property: Id(5)] CachePhysicalRole Role);
