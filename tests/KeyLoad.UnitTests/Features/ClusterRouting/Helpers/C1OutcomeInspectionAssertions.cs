using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ClusterRouting.Processes;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class C1OutcomeInspectionAssertions
{
    private const int SuccessExitCode = 0;
    private const int InvalidRequestExitCode = 2;
    private const string StoreOwnerFileName = "owner.lock";
    internal const string AdminId = "inspection-admin";

    internal static async Task<C1OutcomeInspectionProcessResult> RunAsync(
        C1OutcomeInspectionFixture fixture, Guid? nodeId = null, Guid? incarnation = null,
        string? principalId = null, Guid? commandId = null, PartitionRef? partition = null,
        CancellationToken cancellationToken = default)
    {
        fixture.CloseStore();
        var request = new C1OutcomeInspectionRequest(C1OutcomeInspectionProtocol.Version,
            fixture.DirectoryPath, nodeId ?? fixture.Identity.NodeId, incarnation ?? fixture.Identity.Incarnation,
            principalId ?? AdminId, commandId ?? fixture.CommandId, partition ?? fixture.Partition);
        var input = C1OutcomeInspectionProtocol.SerializeRequest(request);
        var result = await C1OutcomeInspectionProcess.RunAsync(input, fixture.OuterOwnerLockPath, cancellationToken);
        C1OutcomeInspectionOwnerPhase.ObserveChild(fixture, result);
        return result;
    }

    internal static async Task<C1OutcomeInspectionProcessResult> RunRawAsync(
        C1OutcomeInspectionFixture fixture, ReadOnlyMemory<byte> input)
    {
        fixture.CloseStore();
        var result = await C1OutcomeInspectionProcess.RunAsync(input, fixture.OuterOwnerLockPath);
        C1OutcomeInspectionOwnerPhase.ObserveChild(fixture, result);
        return result;
    }

    internal static async Task<C1OutcomeInspectionProcessResult> AssertJoinedAsync(
        C1OutcomeInspectionProcessResult result)
    {
        await Assert.That(result.ProcessReaped).IsTrue();
        await Assert.That(result.InputWriterSettled).IsTrue();
        await Assert.That(result.StandardOutputReaderSettled).IsTrue();
        await Assert.That(result.StandardErrorReaderSettled).IsTrue();
        await Assert.That(result.ProcessHandleClosed).IsTrue();
        await Assert.That(result.OuterOwnerReleased).IsTrue();
        await Assert.That(result.StandardOutputExceeded).IsFalse();
        await Assert.That(result.StandardErrorExceeded).IsFalse();
        return result;
    }

    internal static async Task AssertOutcomeAsync(C1OutcomeInspectionFixture fixture, bool expected,
        string? principalId = null, Guid? commandId = null, PartitionRef? partition = null)
    {
        var result = await AssertJoinedAsync(await RunAsync(fixture, principalId: principalId, commandId: commandId, partition: partition));
        await AssertReceiptAsync(fixture, expected, result);
    }

    internal static async Task AssertReceiptAsync(C1OutcomeInspectionFixture fixture, bool expected,
        C1OutcomeInspectionProcessResult result)
    {
        await Assert.That(result.ExitCode).IsEqualTo(SuccessExitCode);
        await Assert.That(result.StandardError.Length).IsEqualTo(0);
        var receipt = C1OutcomeInspectionProtocol.DeserializeReceipt(result.StandardOutput);
        await Assert.That(receipt.Version).IsEqualTo(C1OutcomeInspectionProtocol.Version);
        await Assert.That(receipt.NodeId).IsEqualTo(fixture.Identity.NodeId);
        await Assert.That(receipt.Incarnation).IsEqualTo(fixture.Identity.Incarnation);
        await Assert.That(receipt.FormatVersion).IsEqualTo(fixture.Identity.FormatVersion);
        await Assert.That(receipt.Position).IsEqualTo(fixture.Position);
        await Assert.That(receipt.OutcomePresent).IsEqualTo(expected);
    }

    internal static async Task AssertRejectedAsync(C1OutcomeInspectionProcessResult result)
    {
        await AssertJoinedAsync(result);
        await Assert.That(result.ExitCode).IsEqualTo(InvalidRequestExitCode);
        await Assert.That(result.StandardOutput.Length).IsEqualTo(0);
        await Assert.That(result.StandardError.Length).IsEqualTo(0);
    }

    internal static byte[] ValidInput(C1OutcomeInspectionFixture fixture)
        => C1OutcomeInspectionProtocol.SerializeRequest(new(C1OutcomeInspectionProtocol.Version,
            fixture.DirectoryPath, fixture.Identity.NodeId, fixture.Identity.Incarnation, AdminId, fixture.CommandId,
            fixture.Partition));

    internal static async Task AssertOuterOwnerReleasedAsync(C1OutcomeInspectionFixture fixture)
    {
        fixture.CloseStore();
        using var outer = C1OutcomeInspectionOwnerPhase.Acquire(fixture,
            C1OutcomeInspectionOwnerRole.ExplicitOuter, fixture.OuterOwnerLockPath);
        using var inner = C1OutcomeInspectionOwnerPhase.Acquire(fixture,
            C1OutcomeInspectionOwnerRole.ExplicitDatabase,
            Path.Combine(fixture.DirectoryPath, StoreOwnerFileName));
        await Assert.That(outer.Length).IsEqualTo(0);
        await Assert.That(inner.Length).IsEqualTo(0);
    }
}
