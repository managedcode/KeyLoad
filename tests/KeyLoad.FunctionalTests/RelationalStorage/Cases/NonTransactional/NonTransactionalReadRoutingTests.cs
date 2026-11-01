using KeyLoad.Core.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.RelationalStorage;

internal sealed class NonTransactionalReadRoutingTests
{
    [Test]
    public async Task OneReadCutKeepsPlainAndAtomicResourcesSeparateAcrossRepeatedLookups()
    {
        using var owner = new NonTransactionalTableFixture();
        (await owner.ConfigureAsync()).Get<ResourceDefinition>();
        (await owner.ConfigureAsync(transactional: true, NonTransactionalTableFixture.AtomicTable)).Get<ResourceDefinition>();
        (await owner.BatchAsync(Guid.NewGuid(), new PutDocument(RelationalTestData.Table, RelationalTestData.First,
            RelationalTestData.Row()))).Get<CommitReceipt>();
        var second = NonTransactionalTableFixture.Row(RelationalTestData.Second, NonTransactionalTableFixture.Beta);
        (await owner.BatchAsync(Guid.NewGuid(), new PutDocument(NonTransactionalTableFixture.AtomicTable,
            RelationalTestData.Second, second))).Get<CommitReceipt>();
        var atomic = DocumentStorageKeys.RecordKey(owner.Partition, NonTransactionalTableFixture.AtomicTable, RelationalTestData.Second);
        var original = owner.Store.Position;
        var result = owner.Store.Read(view => new[]
        {
            view.GetRecord<DocumentRecord>(owner.RowKey())!.Json,
            view.GetRecord<DocumentRecord>(owner.RowKey())!.Json,
            view.GetRecord<DocumentRecord>(atomic)!.Json,
            view.GetRecord<DocumentRecord>(atomic)!.Json,
            view.GetRecord<DocumentRecord>(owner.RowKey())!.Json
        });

        await Assert.That(string.Join('|', result)).IsEqualTo(string.Join('|', RelationalTestData.Row(),
            RelationalTestData.Row(), second, second, RelationalTestData.Row()));
        await Assert.That(owner.Store.Position).IsEqualTo(original);
        await Assert.That(owner.Database.GetDocument(RelationalTestData.Root,
            new(owner.Partition, RelationalTestData.Table, RelationalTestData.First))!.Json).IsEqualTo(RelationalTestData.Row());
    }

    [Test]
    [Arguments("FF")]
    [Arguments("50800000")]
    [Arguments("2002")]
    [Arguments("30")]
    public async Task ReusedReadRoutingStillRejectsEveryMalformedTailAndLeavesTheReadHealthy(string tail)
    {
        using var owner = new NonTransactionalTableFixture();
        (await owner.ConfigureAsync()).Get<ResourceDefinition>();
        (await owner.BatchAsync(Guid.NewGuid(), new PutDocument(RelationalTestData.Table, RelationalTestData.First,
            RelationalTestData.Row()))).Get<CommitReceipt>();
        var key = owner.RowKey();
        var malformed = key.Concat(Convert.FromHexString(tail)).ToArray();
        var code = owner.Store.Read(view =>
        {
            _ = view.ReadValue(key, static _ => { });
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => view.ReadValue(malformed, static _ => { }));
            _ = view.ReadValue(key, static _ => { });
            return failure.Code;
        });
        await Assert.That(code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(owner.Database.GetDocument(RelationalTestData.Root,
            new(owner.Partition, RelationalTestData.Table, RelationalTestData.First))!.Json).IsEqualTo(RelationalTestData.Row());
    }
}
