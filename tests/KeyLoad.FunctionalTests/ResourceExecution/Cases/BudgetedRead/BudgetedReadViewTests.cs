using System.Text;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal sealed class BudgetedReadViewTests
{
    private const string PrefixText = "adapter/";
    private const string FirstKeyText = "adapter/first";
    private const string SecondKeyText = "adapter/second";
    private const string FirstValueText = "first-value";
    private const string SecondValueText = "second-value";
    private static readonly byte[] Prefix = Encoding.UTF8.GetBytes(PrefixText);
    private static readonly byte[] FirstKey = Encoding.UTF8.GetBytes(FirstKeyText);
    private static readonly byte[] SecondKey = Encoding.UTF8.GetBytes(SecondKeyText);
    private static readonly byte[] FirstValue = Encoding.UTF8.GetBytes(FirstValueText);
    private static readonly byte[] SecondValue = Encoding.UTF8.GetBytes(SecondValueText);

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
            var bounded = new ReadExecutionBudget().CreateView(view);
            return bounded.VisitRange(Prefix, 2, static (_, _) => true, cancellationToken: viewCancellation.Token);
        }));
        var budgetError = Assert.ThrowsExactly<OperationCanceledException>(() => database.Store.Read(view =>
        {
            var bounded = new ReadExecutionBudget(cancellationToken: budgetCancellation.Token).CreateView(view);
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
            var bounded = new ReadExecutionBudget(cancellationToken: cancellation.Token).CreateView(view);
            return bounded.VisitRange(Prefix, 2, (_, _) =>
            {
                cancellation.Cancel();
                return true;
            });
        }));
        var viewInterrupted = Assert.ThrowsExactly<OperationCanceledException>(() => database.Store.Read(view =>
        {
            var bounded = new ReadExecutionBudget().CreateView(view);
            return bounded.VisitRange(Prefix, 2, (_, _) =>
            {
                viewCancellation.Cancel();
                return false;
            }, cancellationToken: viewCancellation.Token);
        }));
        var following = database.Store.Read(view => new ReadExecutionBudget().CreateView(view).Scan(Prefix, 2));

        await Assert.That(interrupted.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(viewInterrupted.CancellationToken).IsEqualTo(viewCancellation.Token);
        await Assert.That(following.Records.Length).IsEqualTo(2);
        await Assert.That(following.HasMore).IsFalse();
    }

    private static void SaveRecords(TestDatabase database)
        => database.Store.Commit((transaction, _) =>
        {
            transaction.Put(FirstKey, FirstValue);
            transaction.Put(SecondKey, SecondValue);
            return true;
        });
}
