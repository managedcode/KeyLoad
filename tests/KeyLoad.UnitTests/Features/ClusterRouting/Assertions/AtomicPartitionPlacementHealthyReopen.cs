namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class AtomicPartitionPlacementHealthyReopen
{
    internal static async Task RequireAsync(PhysicalShardCatalogFixture fixture,
        BindAtomicPartitionPlacementRequest request, Guid commandId, AtomicPartitionPlacementResolution witnessBefore, PartitionRef partition,
        Func<PhysicalShardCatalogFixture, BindAtomicPartitionPlacementRequest, Guid, OperationResult> apply)
    {
        var healthy = apply(fixture, request, commandId);
        await Assert.That(healthy.Error).IsNull();
        var expected = witnessBefore with
        {
            DirectoryRevision = 1,
            Revision = 1,
            IsFallback = false
        };
        var bound = fixture.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId,
            AtomicPartitionPlacementTestSupport.Read(partition));
        await Assert.That(NativeSerialization.Serialize(bound)
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        fixture.Reopen();
        var reopened = fixture.Database.ReadAtomicPartitionPlacement(PhysicalShardCatalogFixture.RootPrincipalId,
            AtomicPartitionPlacementTestSupport.Read(partition));
        await Assert.That(NativeSerialization.Serialize(reopened)
            .SequenceEqual(NativeSerialization.Serialize(bound))).IsTrue();
    }

}
