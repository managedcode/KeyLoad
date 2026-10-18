using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class TestDatabaseConcurrentCloseAssertions
{
    internal static async Task ReceiptAsync(Result<object> actual, CommitReceipt expected)
    {
        await Assert.That(Errors.Code(actual.Problem)).IsNull();
        await Assert.That(actual.Problem?.Detail).IsNull();
        
        await Assert.That(actual.Value?.GetType()).IsEqualTo(typeof(CommitReceipt));
        await Assert.That(NativeSerialization.Serialize(actual.Get<CommitReceipt>())
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
    }

    internal static async Task CurrentAsync(DatabaseEngine owner, ZoneTreeStore store, TestDatabaseConcurrentCloseSeed state)
    {
        var reference = new EntityRef(state.Partition, TestDatabaseConcurrentCloseProtocol.Collection,
            TestDatabaseConcurrentCloseProtocol.Document);
        var expected = new DocumentResult(reference, TestDatabaseConcurrentCloseProtocol.Revision,
            TestDatabaseConcurrentCloseProtocol.Body, false, []);
        var actual = owner.GetDocument(TestDatabaseConcurrentCloseProtocol.Principal, reference);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        var expectedRecord = new DocumentRecord(reference, TestDatabaseConcurrentCloseProtocol.Revision,
            TestDatabaseConcurrentCloseProtocol.Body, new RowAccess(), new DateTimeOffset(state.Original.Timestamp));
        var page = store.Read(view => view.Scan(DocumentStorageKeys.Prefix(state.Partition,
            TestDatabaseConcurrentCloseProtocol.Collection), TestDatabaseConcurrentCloseProtocol.PageLimit));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(TestDatabaseConcurrentCloseProtocol.OneRecord);
        var record = page.Records.Single();
        await Assert.That(record.Key.Span.SequenceEqual(DocumentStorageKeys.RecordKey(reference))).IsTrue();
        await Assert.That(record.Value.Span.SequenceEqual(NativeSerialization.Serialize(expectedRecord))).IsTrue();
        await Assert.That(NativeSerialization.Serialize(store.Identity)
            .SequenceEqual(NativeSerialization.Serialize(state.Identity))).IsTrue();
        await Assert.That(store.Position).IsEqualTo(state.LocalPosition);
        await Assert.That(TenantAdmissionAssertions.State(store).SequenceEqual(state.Canonical)).IsTrue();
    }
}
