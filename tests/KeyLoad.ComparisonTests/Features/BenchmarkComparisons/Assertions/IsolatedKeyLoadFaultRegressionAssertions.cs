using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003/004/005: caller-visible persistence, exact idempotency, authority and lease-fence assertions.</summary>
internal static class IsolatedKeyLoadFaultRegressionAssertions
{
    internal static async Task PersistedAsync(KeyLoadClient sdk, IsolatedKeyLoadFaultRegressionSeed seed,
        CancellationToken token)
    {
        await IsolatedKeyLoadPublicRegressionAssertions.DocumentAsync(
            (await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
                attempt => sdk.GetAsync(seed.Sentinel, attempt), token)))!,
            seed.Sentinel, IsolatedKeyLoadFaultRegressionSeed.Payload, 1);
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(seed.SentinelReceipt,
            await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
                attempt => sdk.CommitAsync(seed.SentinelCommand, attempt), token)));
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(seed.AckReceipt,
            await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
                attempt => sdk.CompleteAsync(seed.Ack, attempt), token)));
        await IsolatedKeyLoadPublicRegressionAssertions.ErrorAsync(
            await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
                attempt => sdk.CompleteAsync(seed.Ack with { CommandId = Guid.NewGuid() }, attempt), token), ErrorCode.StaleLease);
        var inspection = await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
            attempt => sdk.InspectAsync(seed.Inspect, attempt), token));
        await Assert.That(inspection!.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(inspection.PayloadJson).IsNull();
        await Assert.That(inspection.HeadersJson).IsNull();
        await Assert.That((await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(
            await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
                attempt => sdk.ReceiveAsync(new(Guid.NewGuid(), seed.Lane), attempt), token))).Deliveries).IsEmpty();
    }

    internal static async Task CommandAsync(KeyLoadClient sdk, CommandRequest command, CommitReceipt receipt,
        EntityRef reference, CancellationToken token)
    {
        await IsolatedKeyLoadPublicRegressionAssertions.ReceiptAsync(receipt, command.CommandId, 1, command.Partition);
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt("putDocument", reference.Collection, reference.Id, 1));
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(receipt,
            await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
                attempt => sdk.CommitAsync(command, attempt), token)));
        await IsolatedKeyLoadPublicRegressionAssertions.DocumentAsync(
            (await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
                attempt => sdk.GetAsync(reference, attempt), token)))!,
            reference, IsolatedKeyLoadFaultRegressionSeed.Payload, 1);
    }

    internal static async Task MembershipAsync(IsolatedKeyLoadFaultRegressionMembership[] baseline,
        IsolatedKeyLoadFaultRegressionMembership[] restored, int killedNode, long position)
    {
        await Assert.That(restored.Length).IsEqualTo(baseline.Length);
        for (var index = 0; index < baseline.Length; index++)
        {
            var before = baseline[index];
            var after = restored[index];
            await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
            await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
            await Assert.That(after.LocalVoter).IsEqualTo(before.LocalVoter);
            await Assert.That(after.Voters.SequenceEqual(before.Voters, StringComparer.Ordinal)).IsTrue();
            await Assert.That(after.Applied).IsGreaterThanOrEqualTo(position);
            await Assert.That(after.ProcessId).IsGreaterThan(0);
            if (after.Index == killedNode)
            {
                await Assert.That(after.ProcessInstance).IsNotEqualTo(before.ProcessInstance);
            }
        }
    }
}
