using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.CommandOutcomes.Execution;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class CanonicalOutcomeReadBudgetFlow
{
    private const int CompleteReads = 3;
    private const int RefusedReads = 2;
    private const int OneByte = 1;
    private const int ColdCuts = 2;
    private const string SeedId = "inspection-outcome-seed";
    private const string Missing = "The genuine original execution outcome is absent.";
    private const string Budget = "The execution outcome validation exhausted its original read budget.";

    internal static async Task RunAsync(CanonicalOutcomeFixture fixture, bool records)
    {
        var store = fixture.Store;
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, fixture.Partition);
        var original = store.Read(view => Select(view, fixture, scope));
        var key = original.Key;
        var locator = KeySpace.OutcomeLocator(fixture.Partition, CanonicalOutcomeFixture.AdminId, fixture.CommandId);
        var raw = store.Read(view => view.ReadOwnedValue(key)) ?? throw new InvalidOperationException(Missing);
        var link = store.Read(view => view.ReadOwnedValue(locator)) ?? throw new InvalidOperationException(Missing);
        var bytes = checked((long)key.Length + raw.Length + 2L * (locator.Length + link.Length));
        var image = CanonicalOutcomeFixture.Image(store);
        var position = store.Position;
        var examined = 0;
        var observedBytes = 0L;
        void Refuse(long actual)
        {
            examined++;
            observedBytes = checked(observedBytes + actual);
            if (records ? examined > RefusedReads : observedBytes > bytes - OneByte)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, Budget);
            }
        }
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => store.Read(view => Select(view, fixture, scope, Refuse)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(examined).IsEqualTo(CompleteReads);
        await CanonicalOutcomeReadBudgetAssertions.UnchangedAsync(store, image, position);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cancellation = Assert.ThrowsExactly<OperationCanceledException>(() => store.Read(view => Select(view,
            fixture, scope, _ => cancelled.Token.ThrowIfCancellationRequested())));
        await Assert.That(cancellation.CancellationToken).IsEqualTo(cancelled.Token);
        await CanonicalOutcomeReadBudgetAssertions.UnchangedAsync(store, image, position);
        examined = 0;
        observedBytes = 0;
        var healthy = store.Read(view => Select(view, fixture, scope, actual =>
        {
            examined++;
            observedBytes = checked(observedBytes + actual);
        }));
        await Assert.That(examined).IsEqualTo(CompleteReads);
        await Assert.That(observedBytes).IsEqualTo(bytes);
        await CanonicalOutcomeReadBudgetAssertions.OriginalAsync(original, healthy, fixture);
        fixture.CommitBatch(fixture.Partition, fixture.CommandId, SeedId);
        await CanonicalOutcomeReadBudgetAssertions.OriginalAsync(original, store.Read(view => Select(view, fixture, scope)), fixture);
        await CanonicalOutcomeReadBudgetAssertions.ImageAsync(store, image);
        await CanonicalOutcomeReadBudgetAssertions.DocumentAsync(fixture.Database, fixture);
        await ColdAsync(fixture, original, image, scope, raw, link);
    }

    private static CommandOutcomeSelection Select(IKeyValueView view, CanonicalOutcomeFixture fixture,
        CommandOutcomePartitionScope scope, StorageReadObserver? observer = null)
        => CommandOutcomeKeyResolver.Select(view, CanonicalOutcomeFixture.AdminId, fixture.CommandId, scope, observer);

    private static async Task ColdAsync(CanonicalOutcomeFixture fixture, CommandOutcomeSelection original,
        string[] image, CommandOutcomePartitionScope scope, byte[] raw, byte[] locator)
    {
        var identity = fixture.Identity;
        fixture.CloseStore();
        for (var cut = 0; cut < ColdCuts; cut++)
        {
            using var reopened = ZoneTreeExistingStore.Open(new ZoneTreeStoreOptions(fixture.DirectoryPath)
            {
                Incarnation = identity.Incarnation
            }, identity.NodeId, UnitExecutionOptions.StorageExecution());
            await CanonicalOutcomeReadBudgetAssertions.IdentityAsync(identity, reopened.Identity);
            await CanonicalOutcomeReadBudgetAssertions.ImageAsync(reopened, image);
            await CanonicalOutcomeReadBudgetAssertions.OriginalAsync(original,
                reopened.Read(view => Select(view, fixture, scope)), fixture);
            await CanonicalOutcomeReadBudgetAssertions.DocumentAsync(
                fixture.Source.CreateConfiguredDatabase(reopened), fixture);
        }
        await fixture.AssertUnrelatedCurrentOperationAsync(raw, locator);
    }
}
