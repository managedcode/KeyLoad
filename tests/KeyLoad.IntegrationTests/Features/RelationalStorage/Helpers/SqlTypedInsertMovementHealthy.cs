using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.RelationalStorage;
internal static class SqlTypedInsertMovementHealthy
{
    internal static async Task<SqlTypedInsertMovementState> RunAsync(TwoRf3MembershipWave wave,SqlTypedInsertMovementTls tls,
        PartitionMovementPublicParentRf3Seed seed,SqlTypedInsertMovementState state,CancellationToken token)
    {
        var request=SqlTypedInsertMovementProtocol.Request(seed.Partition,SqlTypedInsertMovementProtocol.HealthyId,
            SqlTypedInsertMovementProtocol.AfterSql,SqlTypedInsertMovementProtocol.PublishedEpoch);
        CommitReceipt? receipt=null;
        await SqlTypedInsertMovementClients.RunAsync(wave,tls,state,TwoRf3MembershipProtocol.Node4,async(caller,wire)=>
        {
            receipt=await SqlRf3Protocol.SdkAsync<CommitReceipt>(caller.Sdk,request,token);
            await SqlTypedInsertMovementPages.ReceiptAsync(request,receipt,state.PublishedOwner
                ?? throw new InvalidOperationException(SqlTypedInsertMovementProtocol.Missing),SqlTypedInsertMovementProtocol.Two);
            await SqlRf3Protocol.EqualAsync(receipt,await SqlRf3Protocol.McpAsync<CommitReceipt>(caller.Mcp,request,token));
            await SqlTypedInsertMovementClients.ArmAsync(wire,request,token);
            await SqlRf3Protocol.EqualAsync(receipt,await wire.ExecuteAsync<CommitReceipt>(request.Sql,[],prepared:true,token));
            await SqlTypedInsertMovementPages.RowsAsync(seed.Target,caller,wire,seed.Partition,true,receipt,token);
        },token);
        return state with {Healthy=request,HealthyReceipt=receipt ?? throw new InvalidOperationException(SqlTypedInsertMovementProtocol.Missing)};
    }
    internal static Task ReplayAsync(TwoRf3MembershipWave wave,SqlTypedInsertMovementTls tls,
        PartitionMovementPublicParentRf3Seed seed,SqlTypedInsertMovementState state,CancellationToken token)
        => SqlTypedInsertMovementClients.RunAsync(wave,tls,state,TwoRf3MembershipProtocol.Node4,async(caller,wire)=>
    {
        var request=state.Healthy ?? throw new InvalidOperationException(SqlTypedInsertMovementProtocol.Missing);
            var receipt=state.HealthyReceipt ?? throw new InvalidOperationException(SqlTypedInsertMovementProtocol.Missing);
            await SqlRf3Protocol.EqualAsync(receipt,await SqlRf3Protocol.SdkAsync<CommitReceipt>(caller.Sdk,request,token));
            await SqlRf3Protocol.EqualAsync(receipt,await SqlRf3Protocol.McpAsync<CommitReceipt>(caller.Mcp,request,token));
            await SqlTypedInsertMovementClients.ArmAsync(wire,request,token);
            await SqlRf3Protocol.EqualAsync(receipt,await wire.ExecuteAsync<CommitReceipt>(request.Sql,[],prepared:true,token));
            await SqlTypedInsertMovementPages.RowsAsync(seed.Target,caller,wire,seed.Partition,true,receipt,token);
    },token);
}

