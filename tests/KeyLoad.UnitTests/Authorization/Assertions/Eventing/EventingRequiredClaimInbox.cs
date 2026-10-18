using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class EventingRequiredClaimInbox
{
    internal static Task AssertAsync(RequiredProcessingPolicyFixture fixture, ZoneTreeStore store,
        Command<DatabaseCommand> operation, CommitReceipt receipt)
        => RequiredProcessingPolicyAssertions.ObserveAsync(store, async () =>
        {
            const string handler = "throw new InvalidOperationException(\"HANDLER-INJECTION-CANARY\")";
            var request = NativeSerialization.Deserialize<ProcessingRequest>(operation.Value!.Input.Span, NativeValidationProfile.PublicInputElements)!;
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
