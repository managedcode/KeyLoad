using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.RelationalStorage;
internal static class SqlTypedInsertMovementFlow
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave,SqlTypedInsertMovementTls tls,
        PartitionMovementPublicParentRf3Seed seed,CancellationToken token)
    {
        var state=await SqlTypedInsertMovementSeed.RunAsync(wave,seed,token);
        var moved=await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(seed.FirstRequest,token));
        var publication=checked(seed.OriginalPlacement.DirectoryRevision+NextRevision);
        await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seed,seed.FirstRequest,seed.OriginalPlacement,
            seed.Directory.ControlOwner,moved,publication,publication,token);
        var owner=await McpCallerAssertions.SdkSuccessAsync(await seed.Target.ReadAtomicPartitionPlacementAsync(
            new(SqlTypedInsertMovementProtocol.Version,seed.Partition),token));
        await Assert.That(owner.Incarnation).IsEqualTo(moved.DestinationOwner.Incarnation);
        await Assert.That(owner.PlacementEpoch).IsEqualTo(SqlTypedInsertMovementProtocol.PublishedEpoch);
        state=state with {PublishedOwner=owner};
        await SqlTypedInsertMovementConservation.ObserveAsync(seed,()=>SqlTypedInsertMovementReplay.RunAsync(wave,tls,seed,state,token),token);
        await SqlTypedInsertMovementConservation.ObserveAsync(seed,()=>SqlTypedInsertMovementRefusals.RunAsync(wave,tls,seed,state,token),token);
        await SqlTypedInsertMovementCold.RunAsync(wave,tls,seed,state,token);
        state=await SqlTypedInsertMovementPolicy.RunAsync(wave,tls,seed,state,token);
        await SqlTypedInsertMovementConservation.ObserveAsync(seed,()=>SqlTypedInsertMovementCancellation.RunAsync(wave,tls,seed,state,token),token);
        state=await SqlTypedInsertMovementHealthy.RunAsync(wave,tls,seed,state,token);
        await SqlTypedInsertMovementConservation.ObserveAsync(seed,()=>SqlTypedInsertMovementHealthy.ReplayAsync(wave,tls,seed,state,token),token);
        await SqlTypedInsertMovementCold.RunAsync(wave,tls,seed,state,token);
    }
    private const long NextRevision=1;
}

