using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.RelationalStorage;
internal static class SqlTypedInsertMovementConservation
{
    private static async Task<SqlTypedInsertMovementImage> ReadAsync(PartitionMovementPublicParentRf3Seed seed,CancellationToken token)
    {
        KeyLoadClient root=seed.Target;
        var one=await PublicReadAssertions.DocumentAsync(await seed.Target.GetAsync(
            new(seed.Partition,SqlTypedInsertMovementProtocol.Table,SqlTypedInsertMovementProtocol.One),token));
        var two=await PublicReadAssertions.DocumentAsync(await seed.Target.GetAsync(
            new(seed.Partition,SqlTypedInsertMovementProtocol.Table,SqlTypedInsertMovementProtocol.Two),token));
        var outbox=await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(seed.Partition,token));
        var models=await PartitionMovementPublicParentRf3Cut.CaptureAsync(seed,token);
        return new(JsonDefaults.Serialize(new[]{one,two}),JsonDefaults.Serialize(outbox),JsonDefaults.Serialize(models));
    }
    internal static async Task ObserveAsync(PartitionMovementPublicParentRf3Seed seed,Func<Task> operation,CancellationToken token)
    {
        var before=await ReadAsync(seed,token);
        var failures=new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation,failures);
        await ServerFailureObserver.ObserveAsync(async()=>await SqlRf3Protocol.EqualAsync(before,await ReadAsync(seed,token)),failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
