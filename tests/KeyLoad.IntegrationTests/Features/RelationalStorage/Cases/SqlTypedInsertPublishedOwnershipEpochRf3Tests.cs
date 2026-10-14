using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.RelationalStorage;
[NotInParallel]
internal sealed class SqlTypedInsertPublishedOwnershipEpochRf3Tests
{
    [Test]
    public async Task CallerExpectedEpochRetainsOriginalIdentityAndFreshSqlAfterRealTransferAcrossTwoColdCohorts()
    {
        using var deadline=new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline,TimeProvider.System);
        using var caller=CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken,deadline.Token);
        var failures=new List<Exception>();
        TwoRf3MembershipWave? wave=null;
        PartitionMovementPublicParentRf3Seed? seed=null;
        await ServerFailureObserver.ObserveAsync(async()=>
        {
            var partition=new PartitionRef(RelationalSqlRf3Tokens.TenantPrefix+Guid.NewGuid().ToString("N"),
                RelationalSqlRf3Tokens.Database,RelationalSqlRf3Tokens.Domain,Guid.NewGuid().ToString("N"));
            var tls=new SqlTypedInsertMovementTls(partition);
            wave=await TwoRf3MembershipWave.StartOwnedAsync(new TwoRf3MembershipWave(null,register:true,remote:true,
                query:true,probe:true,protectedDocument:true) {SqlInsertTls=tls},caller.Token);
            seed=await PartitionMovementPublicParentRf3Seed.CreateAsync(wave,caller.Token);
            await Assert.That(seed.Partition).IsEqualTo(partition);
            await SqlTypedInsertMovementFlow.RunAsync(wave,tls,seed,caller.Token);
        },failures);
        if(seed is {} ownedSeed)
        {
            await ServerFailureObserver.ObserveAsync(()=>ownedSeed.DisposeAsync().AsTask(),failures);
        }
        if(wave is {} resources)
        {
            await ServerFailureObserver.ObserveAsync(()=>resources.DisposeAsync().AsTask(),failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
