using System.Text;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal sealed class ReadBudgetAllocationTests
{
    private const int LargeValueBytes = 1_048_576;
    private const long RejectionAllocationAllowance = 131_072;
    private const string Prefix = "budget/";
    private const string FirstKey = "budget/1";
    private const string SecondKey = "budget/2";
    private const string MissingKey = "budget/missing";
    private const string EscapedUnicode = "київ café \"line\"\n";
    private const string TemporaryDirectoryPrefix = "keyload-read-budget-";
    private const string RuntimeValueAlias = "keyload.tests.read-budget.value";

    [Test]
    public async Task AcMp002TypedReadsDecodeTheStoredValueWithoutAnIntermediateRawCopy()
    {
        using var fixture = new StoreFixture();
        var expected = new JsonRecord(new string('x', LargeValueBytes));
        var key = Key(FirstKey);
        fixture.Store.Commit((tx, _) => { tx.PutRecord(key, expected); return true; });
        fixture.Store.Read(view => view.GetRecord<JsonRecord>(key));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var actual = fixture.Store.Read(view => view.GetRecord<JsonRecord>(key));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(actual!.Value).IsEqualTo(expected.Value);
        await Assert.That(allocated).IsLessThan(2L * LargeValueBytes + RejectionAllocationAllowance);
        await Assert.That(fixture.Store.Read(view => view.GetRecord<JsonRecord>(Key(MissingKey)))).IsNull();
    }

    [Test]
    public async Task AcMp002CancellationAndReadFailurePreserveTheCommittedStore()
    {
        using var fixture = new StoreFixture();
        var key = Key(FirstKey);
        fixture.Store.Commit((tx, _) => { tx.PutRecord(key, new JsonRecord(EscapedUnicode)); return true; });
        var position = fixture.Store.Position;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var budget = new ReadExecutionBudget(cancellationToken: cancellation.Token);

        await Assert.That(() => fixture.Store.Read(view => budget.Read(view, key))).Throws<OperationCanceledException>();
        await Assert.That(() => fixture.Store.Read(view => budget.Scan(view, Key(Prefix), 1))).Throws<OperationCanceledException>();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        fixture.Store.Commit((tx, _) => { tx.PutRecord(Key(SecondKey), new JsonRecord(EscapedUnicode)); return true; });
        await Assert.That(fixture.Store.Read(view => view.Scan(Key(Prefix), 2).Records.Length)).IsEqualTo(2);
    }

    private static byte[] Key(string value) => Encoding.UTF8.GetBytes(value);
    [global::Orleans.GenerateSerializer, global::Orleans.Alias(RuntimeValueAlias)]
    internal sealed record JsonRecord([property: global::Orleans.Id(0)] string Value);

    private sealed class StoreFixture : IDisposable
    {
        private readonly string directory = Path.Combine(AppContext.BaseDirectory, TemporaryDirectoryPrefix + Guid.NewGuid().ToString("N"));
        public ZoneTreeStore Store
        {
            get;
        }
        public StoreFixture() => Store = new(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        public void Dispose()
        {
            Store.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }
}
