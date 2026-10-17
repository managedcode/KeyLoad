using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class EventingRequiredClaimInbox
{
    internal static Task AssertAsync(RequiredProcessingPolicyFixture fixture, ZoneTreeStore store,
        ReplicatedOperation operation, CommitReceipt receipt)
        => RequiredProcessingPolicyAssertions.ObserveAsync(store, async () =>
        {
            const string handler = "throw new InvalidOperationException(\"HANDLER-INJECTION-CANARY\")";
            var request = JsonSerializer.Deserialize<ProcessingRequest>(operation.PayloadJson, JsonDefaults.Options)!;
            await Assert.That(request.HandlerScope).IsEqualTo(handler);
            var key = KeySpace.Partition("inbox", fixture.Harness.Partition, RequiredProcessingPolicyFixture.Queue,
                "first", handler, 1L);
            var inbox = store.Read(view => view.GetRecord<InboxRecord>(key))!;
            var fingerprint = JsonData.Fingerprint(new
            {
                Lane = fixture.Lane,
                MessageId = "first",
                HandlerScope = handler,
                ExecutionGeneration = 1L,
                Effects = request.Effects,
                Id = RequiredProcessingPolicyFixture.Worker
            });
            await Assert.That(inbox.Fingerprint).IsEqualTo(fingerprint);
            await RequiredProcessingPolicyAssertions.ReceiptAsync(receipt, inbox.Receipt);
        });
}
