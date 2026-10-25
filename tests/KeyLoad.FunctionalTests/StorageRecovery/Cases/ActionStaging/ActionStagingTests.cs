using System.Text;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

/// <summary>AC-REP-008: actions sharing one commit keep their own reset point and frame validation.</summary>
internal sealed class ActionStagingTests
{
    private static readonly byte[] FirstKey = Encoding.UTF8.GetBytes("action-staging/first");
    private static readonly byte[] DiscardedKey = Encoding.UTF8.GetBytes("action-staging/discarded");
    private static readonly byte[] SecondKey = Encoding.UTF8.GetBytes("action-staging/second");
    private static readonly byte[] ChildKey = Encoding.UTF8.GetBytes("action-staging/child");
    private static readonly byte[] FirstValue = Encoding.UTF8.GetBytes("first");
    private static readonly byte[] ReplacedValue = Encoding.UTF8.GetBytes("replaced");
    private static readonly byte[] SecondValue = Encoding.UTF8.GetBytes("second");
    private const long OneFrame = 1;

    [Test]
    public Task AcRep008ResetDiscardsOnlyTheCurrentActionAndEveryActionCommitsInOneFrame()
        => PreparationOwnershipOwner.RunAsync(async owner =>
        {
            var position = owner.Store.Position;
            var observed = owner.Store.Commit((transaction, _) =>
            {
                var staging = (IAtomicActionStaging)transaction;
                transaction.Put(FirstKey, FirstValue);
                staging.BeginAction();
                transaction.Put(DiscardedKey, SecondValue);
                transaction.Put(FirstKey, ReplacedValue);
                transaction.Reset();
                var afterReset = (First: transaction.ReadOwnedValue(FirstKey), Discarded: transaction.ReadOwnedValue(DiscardedKey));
                transaction.Put(SecondKey, SecondValue);
                var child = transaction.Prepare(prepared =>
                {
                    prepared.Put(ChildKey, FirstValue);
                    prepared.Reset();
                    return (First: prepared.ReadOwnedValue(FirstKey), Second: prepared.ReadOwnedValue(SecondKey),
                        Child: prepared.ReadOwnedValue(ChildKey));
                });
                transaction.ValidateCommit();
                return (AfterReset: afterReset, Child: child, Fits: staging.FitsOneFrame);
            });

            await Assert.That(observed.AfterReset.First).IsEquivalentTo(FirstValue, CollectionOrdering.Matching);
            await Assert.That(observed.AfterReset.Discarded).IsNull();
            await Assert.That(observed.Child.First).IsEquivalentTo(FirstValue, CollectionOrdering.Matching);
            await Assert.That(observed.Child.Second).IsEquivalentTo(SecondValue, CollectionOrdering.Matching);
            await Assert.That(observed.Child.Child).IsNull();
            await Assert.That(observed.Fits).IsTrue();
            await Assert.That(owner.Store.Position).IsEqualTo(position + OneFrame);
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(FirstKey))).IsEquivalentTo(FirstValue, CollectionOrdering.Matching);
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(SecondKey))).IsEquivalentTo(SecondValue, CollectionOrdering.Matching);
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(DiscardedKey))).IsNull();
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(ChildKey))).IsNull();
        });

    [Test]
    public async Task AcRep008ActionFrameBudgetIgnoresEarlierActionsWhileTheSharedFrameReportsTheUnion()
    {
        StorageMutation[] one = [new(SecondKey, new byte[FrameValueBytes])];
        var frame = ZoneTreeJournalCodec.Serialize(one, int.MaxValue).Length;
        var root = Path.Combine(AppContext.BaseDirectory, "keyload-action-staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new ZoneTreeStore(new(root) { MaxFrameBytes = frame + FrameSlack },
                UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            var fits = store.Commit((transaction, _) =>
            {
                var staging = (IAtomicActionStaging)transaction;
                transaction.Put(FirstKey, new byte[FrameValueBytes]);
                staging.BeginAction();
                transaction.Put(SecondKey, new byte[FrameValueBytes]);
                transaction.ValidateCommit();
                var union = staging.FitsOneFrame;
                transaction.Reset();
                return (Union: union, AfterReset: staging.FitsOneFrame);
            });

            await Assert.That(fits.Union).IsFalse();
            await Assert.That(fits.AfterReset).IsTrue();
            await Assert.That(store.Read(view => view.ReadOwnedValue(FirstKey))!.Length).IsEqualTo(FrameValueBytes);
            await Assert.That(store.Read(view => view.ReadOwnedValue(SecondKey))).IsNull();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private const int FrameValueBytes = 8_192;
    private const int FrameSlack = 4_096 + 64 * 2;
}
