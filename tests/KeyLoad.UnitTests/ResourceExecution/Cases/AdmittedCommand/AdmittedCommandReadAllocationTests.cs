using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class AdmittedCommandReadAllocationTests
{
    private const string PrincipalId = "allocation-principal";
    private const string TenantId = "allocation-tenant";
    private const string EmptyOperationJson = "{}";
    private const int WarmupCommandCount = 32;
    private const int MeasuredCommandCount = 256;
    private const int TotalCommandCount = WarmupCommandCount + MeasuredCommandCount;
    private const int MaximumReadWindowAllocationsBytes = 512;
    private const int PayloadBytes = 2;
    private const string SuccessfulResultJson = "true";
    private const string ExpectedSynchronousReadDetail = "The prefilled inbox read should be synchronous.";
    private const string UnexpectedStoppedInboxDetail = "The prefilled inbox unexpectedly stopped.";

    [Test]
    public async Task WarmedPrefilledInboxReadsWithinSmallAllocationWindow()
    {
        var governor = new CommandAdmissionGovernor(UnitAdmissionOptions.Command(new()
        {
            MaxCommands = TotalCommandCount,
            MaxTenantCommands = TotalCommandCount,
            MaxPrincipalCommands = TotalCommandCount
        }));
        await using var inbox = new AdmittedCommandInbox(governor, UnitAdmissionOptions.Inbox());
        var principal = new PrincipalRecord(PrincipalId, TenantId, [], []);
        var commandCount = TotalCommandCount;
        FillInbox(inbox, principal, commandCount);

        for (var index = 0; index < WarmupCommandCount; index++)
        {
            var warmupCommand = ReadAvailable(inbox);
            warmupCommand.Complete(new(SuccessfulResultJson));
        }

        var measuredCommands = new AdmittedCommand[MeasuredCommandCount];
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < measuredCommands.Length; index++)
        {
            measuredCommands[index] = ReadAvailable(inbox);
        }
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        for (var index = 0; index < measuredCommands.Length; index++)
        {
            measuredCommands[index].Complete(new(SuccessfulResultJson));
        }

        await Assert.That(allocatedBytes).IsLessThanOrEqualTo(MaximumReadWindowAllocationsBytes);
        await Assert.That(governor.Snapshot().Commands).IsEqualTo(0);
    }

    private static void FillInbox(AdmittedCommandInbox inbox, PrincipalRecord principal, int count)
    {
        for (var index = 0; index < count; index++)
        {
            var operation = new ReplicatedOperation(Guid.NewGuid(), OperationKind.Batch, PrincipalId,
                TimeProvider.System.GetUtcNow(), EmptyOperationJson);
            inbox.Enqueue(operation, principal, PayloadBytes);
        }
    }

    private static AdmittedCommand ReadAvailable(AdmittedCommandInbox inbox)
    {
        var pending = inbox.ReadAsync();
        if (!pending.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException(ExpectedSynchronousReadDetail);
        }

        return pending.Result ?? throw new InvalidOperationException(UnexpectedStoppedInboxDetail);
    }
}
