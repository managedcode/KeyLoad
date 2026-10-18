using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003/004/005: caller-visible persistence, exact idempotency, authority and lease-fence assertions.</summary>
internal static class IsolatedKeyLoadFaultChecksAssertions
{
    internal static async Task PersistedAsync(KeyLoadClient sdk, IsolatedKeyLoadFaultChecksSeed seed,
        CancellationToken token)
    {
        await IsolatedKeyLoadPublicChecksAssertions.DocumentAsync(
            (await IsolatedKeyLoadPublicChecksAssertions.DocumentReadAsync(await IsolatedKeyLoadFaultChecksCalls.RunAsync(
                attempt => sdk.GetAsync(seed.Sentinel, attempt), token)))!,
            seed.Sentinel, IsolatedKeyLoadFaultChecksSeed.Payload, 1);
        await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(seed.SentinelReceipt,
            await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await IsolatedKeyLoadFaultChecksCalls.RunAsync(
                attempt => sdk.CommitAsync(seed.SentinelCommand, attempt), token)));
        await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(seed.AckReceipt,
            await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await IsolatedKeyLoadFaultChecksCalls.RunAsync(
                attempt => sdk.CompleteAsync(seed.Ack, attempt), token)));
        await IsolatedKeyLoadPublicChecksAssertions.ErrorAsync(
            await IsolatedKeyLoadFaultChecksCalls.RunAsync(
                attempt => sdk.CompleteAsync(seed.Ack with { CommandId = Guid.NewGuid() }, attempt), token), ErrorCode.StaleLease);
        var inspection = await IsolatedKeyLoadPublicChecksAssertions.MessageReadAsync(await IsolatedKeyLoadFaultChecksCalls.RunAsync(
            attempt => sdk.InspectAsync(seed.Inspect, attempt), token));
        await Assert.That(inspection!.Metadata.State).IsEqualTo(MessageState.Acked);
        await Assert.That(inspection.PayloadJson).IsNull();
        await Assert.That(inspection.HeadersJson).IsNull();
        await Assert.That((await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(
            await IsolatedKeyLoadFaultChecksCalls.RunAsync(
                attempt => sdk.ReceiveAsync(new(Guid.NewGuid(), seed.Lane), attempt), token))).Deliveries).IsEmpty();
    }

    internal static async Task CommandAsync(KeyLoadClient sdk, CommandRequest command, CommitReceipt receipt,
        EntityRef reference, int expectedCopies, CancellationToken token)
    {
        await IsolatedKeyLoadPublicChecksAssertions.ReceiptAsync(receipt, command.CommandId, 1, command.Partition, expectedCopies);
        await Assert.That(receipt.Mutations[0]).IsEqualTo(new MutationReceipt("putDocument", reference.Collection, reference.Id, 1));
        await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(receipt,
            await IsolatedKeyLoadPublicChecksAssertions.SuccessAsync(await IsolatedKeyLoadFaultChecksCalls.RunAsync(
                attempt => sdk.CommitAsync(command, attempt), token)));
        await IsolatedKeyLoadPublicChecksAssertions.DocumentAsync(
            (await IsolatedKeyLoadPublicChecksAssertions.DocumentReadAsync(await IsolatedKeyLoadFaultChecksCalls.RunAsync(
                attempt => sdk.GetAsync(reference, attempt), token)))!,
            reference, IsolatedKeyLoadFaultChecksSeed.Payload, 1);
    }

    internal static async Task MembershipAsync(IsolatedKeyLoadFaultChecksMembership[] baseline,
        IsolatedKeyLoadFaultChecksMembership[] restored, int killedNode)
    {
        await Assert.That(restored.Length).IsEqualTo(baseline.Length);
        for (var index = 0; index < baseline.Length; index++)
        {
            var before = baseline[index];
            var after = restored[index];
            await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
            await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
            await Assert.That(after.SiloName).IsEqualTo(before.SiloName);
            await Assert.That(after.ActiveSilos.SequenceEqual(before.ActiveSilos, StringComparer.Ordinal)).IsTrue();
            await Assert.That(after.ProcessId).IsGreaterThan(0);
            if (after.Index == killedNode)
            {
                await Assert.That(after.ProcessInstance).IsNotEqualTo(before.ProcessInstance);
            }
        }
    }
}
