namespace KeyLoad.Storage.ZoneTree;

internal sealed record ZoneTreeRetirementConservationProof(
    ResourceRetirementConservationFloorV1? Floor, long AuthorityRecords, ReadOnlyMemory<byte> Digest);
