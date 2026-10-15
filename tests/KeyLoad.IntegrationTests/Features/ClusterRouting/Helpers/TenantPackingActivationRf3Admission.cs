using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantPackingActivationRf3Admission
{
    private const int Version = 1;
    private const long InitialDirectoryRevision = 0;
    private const long FirstDirectoryRevision = 1;
    private const long SecondDirectoryRevision = 2;
    private const long InitialRowRevision = 1;
    private const string StaleDetail = "The atomic partition placement revision is stale.";

    internal static async Task<(AtomicPartitionPlacementResolution First, AtomicPartitionPlacementResolution Second)> BindAsync(
        RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers first, RequestCqrsPhaseFaultIdentity firstIdentity,
        RequestCqrsPhaseFaultIdentity secondIdentity, CancellationToken token)
    {
        var fallback = await ReadAsync(administrator, firstIdentity.Partition, token);
        await Assert.That(fallback.IsFallback).IsTrue();
        await Assert.That(fallback.DirectoryRevision).IsEqualTo(InitialDirectoryRevision);
        var firstRequest = new BindAtomicPartitionPlacementRequest(Version, InitialDirectoryRevision,
            firstIdentity.Partition, fallback.PhysicalShardId);
        var deniedId = Guid.NewGuid();
        var denied = await first.Sdk.BindAtomicPartitionPlacementAsync(deniedId, firstRequest, token);
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await Assert.That(denied.Value).IsFalse();
        await Assert.That(denied.IsSuccess).IsFalse();
        await PartitionMovementPublicRefusalRf3Oracle.SdkAsync(denied.Problem,
            ErrorCode.PermissionDenied, PartitionMovementPublicRefusalRf3Oracle.AdministrationDetail);
        await SqlRf3Protocol.EqualAsync(fallback, await ReadAsync(administrator, firstIdentity.Partition, token));
        var deniedOfficial = await first.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementBind, new
        {
            commandId = deniedId,
            request = firstRequest
        }, token);
        await PartitionMovementPublicRefusalRf3Oracle.McpAsync(deniedOfficial, ErrorCode.PermissionDenied,
            PartitionMovementPublicRefusalRf3Oracle.SafePermissionDetail);
        await SqlRf3Protocol.EqualAsync(fallback, await ReadAsync(administrator, firstIdentity.Partition, token));
        await McpCallerAssertions.ErrorAsync(await first.Mcp.CallAsync(AtomicPartitionPlacementPublicRf3Protocol.ReadTool,
            new AtomicPartitionPlacementReadRequest(Version, firstIdentity.Partition), token),
            ErrorCode.PermissionDenied, dispatched: true);
        await SqlRf3Protocol.EqualAsync(fallback, await ReadAsync(administrator, firstIdentity.Partition, token));
        await BindAsync(administrator, Guid.NewGuid(), firstRequest, token);
        var firstPlacement = await ReadAsync(administrator, firstIdentity.Partition, token);
        var stale = new BindAtomicPartitionPlacementRequest(Version, InitialDirectoryRevision,
            secondIdentity.Partition, fallback.PhysicalShardId);
        var staleId = Guid.NewGuid();
        await ConflictAsync(administrator, staleId, stale, token);
        var untouched = await ReadAsync(administrator, secondIdentity.Partition, token);
        await Assert.That(untouched.IsFallback).IsTrue();
        await Assert.That(untouched.DirectoryRevision).IsEqualTo(FirstDirectoryRevision);
        await BindAsync(administrator, Guid.NewGuid(), stale with
        {
            ExpectedRevision = FirstDirectoryRevision
        }, token);
        await ConflictAsync(administrator, staleId, stale, token);
        var firstCurrent = await ReadAsync(administrator, firstIdentity.Partition, token);
        var secondCurrent = await ReadAsync(administrator, secondIdentity.Partition, token);
        await Assert.That(firstCurrent.DirectoryRevision).IsEqualTo(SecondDirectoryRevision);
        await Assert.That(secondCurrent.DirectoryRevision).IsEqualTo(SecondDirectoryRevision);
        await Assert.That(firstCurrent.Revision).IsEqualTo(firstPlacement.Revision);
        await TenantPackingActivationRf3Assertions.SamePhysicalAsync(firstCurrent, secondCurrent);
        return (firstCurrent, secondCurrent);
    }

    internal static async Task<AtomicPartitionPlacementResolution> ReadAsync(RequestCqrsRf3Callers administrator,
        PartitionRef partition, CancellationToken token)
    {
        var request = new AtomicPartitionPlacementReadRequest(Version, partition);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
        var mcp = await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await administrator.Mcp.CallAsync(AtomicPartitionPlacementPublicRf3Protocol.ReadTool, request, token));
        await SqlRf3Protocol.EqualAsync(sdk, mcp.Value);
        return sdk;
    }

    private static async Task BindAsync(RequestCqrsRf3Callers administrator, Guid id,
        BindAtomicPartitionPlacementRequest request, CancellationToken token)
    {
        var before = await ReadAsync(administrator, request.Partition, token);
        var expected = before with
        {
            Revision = InitialRowRevision,
            DirectoryRevision = checked(request.ExpectedRevision + FirstDirectoryRevision),
            IsFallback = false
        };
        var actual = await administrator.Sdk.BindAtomicPartitionPlacementAsync(id, request, token);
        await Assert.That(actual.IsSuccess).IsTrue();
        await Assert.That(actual.Value).IsTrue();
        await SqlRf3Protocol.EqualAsync(expected, await ReadAsync(administrator, request.Partition, token));
        var replay = await administrator.Sdk.BindAtomicPartitionPlacementAsync(id, request, token);
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Value).IsTrue();
        await SqlRf3Protocol.EqualAsync(expected, await ReadAsync(administrator, request.Partition, token));
        var official = await McpCallerAssertions.SuccessAsync<bool>(await administrator.Mcp.CallAsync(
            McpCallerTools.AdminPartitionPlacementBind, new
            {
                commandId = id,
                request
            }, token));
        await Assert.That(official.Value).IsTrue();
        await SqlRf3Protocol.EqualAsync(expected, await ReadAsync(administrator, request.Partition, token));
    }

    private static async Task ConflictAsync(RequestCqrsRf3Callers administrator, Guid id,
        BindAtomicPartitionPlacementRequest request, CancellationToken token)
    {
        var before = await ReadAsync(administrator, request.Partition, token);
        var result = await administrator.Sdk.BindAtomicPartitionPlacementAsync(id, request, token);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Conflict));
        await Assert.That(result.Value).IsFalse();
        await PartitionMovementPublicRefusalRf3Oracle.SdkAsync(result.Problem, ErrorCode.Conflict, StaleDetail);
        await SqlRf3Protocol.EqualAsync(before, await ReadAsync(administrator, request.Partition, token));
        var official = await administrator.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementBind, new
        {
            commandId = id,
            request
        }, token);
        await PartitionMovementPublicRefusalRf3Oracle.McpAsync(official, ErrorCode.Conflict,
            PartitionMovementPublicRefusalRf3Oracle.SafeGenericDetail);
        await SqlRf3Protocol.EqualAsync(before, await ReadAsync(administrator, request.Partition, token));
    }
}
