using System.Text;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ReadBudgetAllocationTests
{
    private const int LargeValueBytes = 1_048_576;
    private const long RejectionAllocationAllowance = 131_072;
    private const long SerializationAllocationAllowance = 262_144;
    private const int SmallReadBudget = 16;
    private const int ResultParts = 4_096;
    private const int ResultPartCharacters = 512;
    private const string Prefix = "budget/";
    private const string FirstKey = "budget/1";
    private const string SecondKey = "budget/2";
    private const string ThirdKey = "budget/3";
    private const string MissingKey = "budget/missing";
    private const string EscapedUnicode = "київ café \"line\"\n";
    private const string TemporaryDirectoryPrefix = "keyload-read-budget-";

    [Test]
    public async Task AcMp002PointLimitRejectsBeforeAllocatingAnOwnedLargeValue()
    {
        using var fixture = new StoreFixture();
        var key = Key(FirstKey);
        fixture.Store.Commit((tx, _) => { tx.Put(key, new byte[LargeValueBytes]); return true; });
        var limits = new DatabaseLimits { MaxQueryReadBytes = SmallReadBudget };
        var token = TestContext.Current!.Execution.CancellationToken;
        Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view => new ReadExecutionBudget(limits, cancellationToken: token).Read(view, key)));

        var budget = new ReadExecutionBudget(limits, cancellationToken: token);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view => budget.Read(view, key)));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(allocated).IsLessThan(RejectionAllocationAllowance);
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(key)!.Length)).IsEqualTo(LargeValueBytes);
    }

    [Test]
    public async Task AcMp002RangeLimitRejectsBeforeMaterializingTheWholePage()
    {
        using var fixture = new StoreFixture();
        fixture.Store.Commit((tx, _) =>
        {
            foreach (var key in new[] { FirstKey, SecondKey, ThirdKey })
            {
                tx.Put(Key(key), new byte[LargeValueBytes]);
            }
            return true;
        });
        var limits = new DatabaseLimits { MaxQueryReadBytes = SmallReadBudget };
        var prefix = Key(Prefix);
        var token = TestContext.Current!.Execution.CancellationToken;
        Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view => new ReadExecutionBudget(limits, cancellationToken: token).Scan(view, prefix, 3)));

        var budget = new ReadExecutionBudget(limits, cancellationToken: token);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Store.Read(view => budget.Scan(view, prefix, 3)));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(allocated).IsLessThan(RejectionAllocationAllowance);
        await Assert.That(fixture.Store.Read(view => view.Scan(prefix, 3).Records.Length)).IsEqualTo(3);
    }

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
    public async Task AcMp005ResultLimitIncludesExactUnicodeEscapingAndProtocolMetadata()
    {
        var result = new JsonRecord(EscapedUnicode);
        var bytes = JsonDefaults.Serialize(result).Length;
        new ReadExecutionBudget(new() { MaxBatchBytes = bytes }).CheckResult(result);
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            new ReadExecutionBudget(new() { MaxBatchBytes = bytes - 1 }).CheckResult(result));

        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcMp005ResultCountingDoesNotAllocateACompleteSerializedResultBuffer()
    {
        var result = Enumerable.Repeat(new string('x', ResultPartCharacters), ResultParts).ToArray();
        var limits = new DatabaseLimits();
        new ReadExecutionBudget(limits).CheckResult(result);
        var budget = new ReadExecutionBudget(limits, cancellationToken: TestContext.Current!.Execution.CancellationToken);

        var before = GC.GetAllocatedBytesForCurrentThread();
        budget.CheckResult(result);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsLessThan(SerializationAllocationAllowance);
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
        var budget = new ReadExecutionBudget(new(), cancellationToken: cancellation.Token);

        await Assert.That(() => fixture.Store.Read(view => budget.Read(view, key))).Throws<OperationCanceledException>();
        await Assert.That(() => fixture.Store.Read(view => budget.Scan(view, Key(Prefix), 1))).Throws<OperationCanceledException>();
        await Assert.That(() => budget.CheckResult(new JsonRecord(EscapedUnicode))).Throws<OperationCanceledException>();
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        fixture.Store.Commit((tx, _) => { tx.PutRecord(Key(SecondKey), new JsonRecord(EscapedUnicode)); return true; });
        await Assert.That(fixture.Store.Read(view => view.Scan(Key(Prefix), 2).Records.Length)).IsEqualTo(2);
    }

    private static byte[] Key(string value) => Encoding.UTF8.GetBytes(value);
    private sealed record JsonRecord(string Value);

    private sealed class StoreFixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), TemporaryDirectoryPrefix + Guid.NewGuid().ToString("N"));
        public ZoneTreeStore Store { get; }
        public StoreFixture() => Store = new(new(directory));
        public void Dispose()
        {
            Store.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }
}
