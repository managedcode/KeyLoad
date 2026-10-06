using System.Text;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class BudgetedReadViewTests
{
    private const string PrefixText = "adapter/";
    private const string FirstKeyText = "adapter/first";
    private const string SecondKeyText = "adapter/second";
    private const string FirstValueText = "first-value";
    private const string SecondValueText = "second-value";
    private const string MissingKeyText = "adapter/missing";
    private static readonly byte[] Prefix = Encoding.UTF8.GetBytes(PrefixText);
    private static readonly byte[] FirstKey = Encoding.UTF8.GetBytes(FirstKeyText);
    private static readonly byte[] SecondKey = Encoding.UTF8.GetBytes(SecondKeyText);
    private static readonly byte[] FirstValue = Encoding.UTF8.GetBytes(FirstValueText);
    private static readonly byte[] SecondValue = Encoding.UTF8.GetBytes(SecondValueText);

    [Test]
    public async Task AcMp005PointReadsChargeBeforeCallbacksAndChargeMissingKeys()
    {
        using var database = new TestDatabase();
        SaveRecords(database);
        var observation = database.Store.Read(view =>
        {
            var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new()));
            var bounded = budget.CreateView(view);
            var foundObserverBudget = 0L;
            byte[]? foundValue = null;
            var found = bounded.ReadValue(FirstKey, value => foundValue = value.ToArray(), _ => foundObserverBudget = budget.ReadBytes);
            var missingObserverBudget = 0L;
            var missing = bounded.ReadValue(Encoding.UTF8.GetBytes(MissingKeyText), static _ => { },
                _ => missingObserverBudget = budget.ReadBytes);
            return (budget.ReadBytes, foundObserverBudget, missingObserverBudget, foundValue, found);
        });
        var expectedFound = FirstKey.Length + FirstValue.Length;
        var expectedTotal = expectedFound + Encoding.UTF8.GetByteCount(MissingKeyText);

        await Assert.That(observation.found).IsTrue();
        await Assert.That(observation.foundValue).IsEquivalentTo(FirstValue);
        await Assert.That(observation.foundObserverBudget).IsEqualTo(expectedFound);
        await Assert.That(observation.missingObserverBudget).IsEqualTo(expectedTotal);
        await Assert.That(observation.ReadBytes).IsEqualTo(expectedTotal);
    }

    [Test]
    public async Task AcMp005RangeAndScanPreserveLookaheadAndChargeBeforeVisitor()
    {
        using var database = new TestDatabase();
        SaveRecords(database);
        var observation = database.Store.Read(view =>
        {
            var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new()));
            var bounded = budget.CreateView(view);
            var chargedAtVisitor = 0L;
            var range = bounded.VisitRange(Prefix, 1, (_, _) =>
            {
                chargedAtVisitor = budget.ReadBytes;
                return true;
            });
            var page = bounded.Scan(Prefix, 1);
            return (ReadBytes: budget.ReadBytes, chargedAtVisitor, RangeHasMore: range.HasMore,
                PageHasMore: page.HasMore, PageRecords: page.Records);
        });

        await Assert.That(observation.RangeHasMore).IsTrue();
        await Assert.That(observation.PageHasMore).IsTrue();
        await Assert.That(observation.PageRecords).HasSingleItem();
        await Assert.That(observation.chargedAtVisitor).IsGreaterThan(0L);
        await Assert.That(observation.ReadBytes).IsGreaterThan(observation.chargedAtVisitor);
    }

    [Test]
    public async Task AcMp005PointBudgetRejectsOversizedRawValueBeforeReturningIt()
    {
        using var database = new TestDatabase();
        SaveRecords(database);
        var limits = new DatabaseLimits { MaxQueryReadBytes = FirstKey.Length + FirstValue.Length - 1 };
        var error = Assert.ThrowsExactly<KeyLoadException>(() => database.Store.Read(view =>
        {
            var bounded = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(limits)).CreateView(view);
            return bounded.ReadOwnedValue(FirstKey);
        }));

        await Assert.That(error.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcMp005RangeHonorsViewAndBudgetCancellationTokens()
    {
        using var database = new TestDatabase();
        SaveRecords(database);
        using var viewCancellation = new CancellationTokenSource();
        using var budgetCancellation = new CancellationTokenSource();
        await viewCancellation.CancelAsync();
        await budgetCancellation.CancelAsync();
        var viewError = Assert.ThrowsExactly<OperationCanceledException>(() => database.Store.Read(view =>
        {
            var bounded = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new())).CreateView(view);
            return bounded.VisitRange(Prefix, 2, static (_, _) => true, cancellationToken: viewCancellation.Token);
        }));
        var budgetError = Assert.ThrowsExactly<OperationCanceledException>(() => database.Store.Read(view =>
        {
            var bounded = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new()), cancellationToken: budgetCancellation.Token).CreateView(view);
            return bounded.VisitRange(Prefix, 2, static (_, _) => true);
        }));

        await Assert.That(viewError.CancellationToken).IsEqualTo(viewCancellation.Token);
        await Assert.That(budgetError.CancellationToken).IsEqualTo(budgetCancellation.Token);
    }

    [Test]
    public async Task AcMp005CancellationDuringARealRangeStopsTraversalAndLeavesTheStoreUsable()
    {
        using var database = new TestDatabase();
        SaveRecords(database);
        using var cancellation = new CancellationTokenSource();
        using var viewCancellation = new CancellationTokenSource();
        var interrupted = Assert.ThrowsExactly<OperationCanceledException>(() => database.Store.Read(view =>
        {
            var bounded = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new()), cancellationToken: cancellation.Token).CreateView(view);
            return bounded.VisitRange(Prefix, 2, (_, _) =>
            {
                cancellation.Cancel();
                return true;
            });
        }));
        var viewInterrupted = Assert.ThrowsExactly<OperationCanceledException>(() => database.Store.Read(view =>
        {
            var bounded = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new())).CreateView(view);
            return bounded.VisitRange(Prefix, 2, (_, _) =>
            {
                viewCancellation.Cancel();
                return false;
            }, cancellationToken: viewCancellation.Token);
        }));
        var following = database.Store.Read(view => new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new())).CreateView(view).Scan(Prefix, 2));

        await Assert.That(interrupted.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(viewInterrupted.CancellationToken).IsEqualTo(viewCancellation.Token);
        await Assert.That(following.Records.Length).IsEqualTo(2);
        await Assert.That(following.HasMore).IsFalse();
    }

    [Test]
    public async Task AcMp005ObserverCancellationStopsBeforePointConsumerAndAllowsHealthyRead()
    {
        using var database = new TestDatabase();
        SaveRecords(database);
        using var cancellation = new CancellationTokenSource();
        var consumed = false;
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => database.Store.Read(view =>
        {
            var bounded = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new()), cancellationToken: cancellation.Token).CreateView(view);
            return bounded.ReadValue(FirstKey, _ => consumed = true, _ => cancellation.Cancel());
        }));
        var healthy = database.Store.Read(view => new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new())).CreateView(view)
            .ReadValue(FirstKey, static _ => { }));

        await Assert.That(error.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(consumed).IsFalse();
        await Assert.That(healthy).IsTrue();
    }

    private static void SaveRecords(TestDatabase database)
        => database.Store.Commit((transaction, _) =>
        {
            transaction.Put(FirstKey, FirstValue);
            transaction.Put(SecondKey, SecondValue);
            return true;
        });
}
