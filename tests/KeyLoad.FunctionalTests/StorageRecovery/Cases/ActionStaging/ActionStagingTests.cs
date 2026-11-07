using System.Text;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

/// <summary>AC-REP-008: actions sharing one commit keep their own reset point inside one ZoneTree transaction.</summary>
internal sealed class ActionStagingTests
{
    private static readonly byte[] FirstKey = Encoding.UTF8.GetBytes("action-staging/first");
    private static readonly byte[] DiscardedKey = Encoding.UTF8.GetBytes("action-staging/discarded");
    private static readonly byte[] SecondKey = Encoding.UTF8.GetBytes("action-staging/second");
    private static readonly byte[] ChildKey = Encoding.UTF8.GetBytes("action-staging/child");
    private static readonly byte[] FirstValue = Encoding.UTF8.GetBytes("first");
    private static readonly byte[] ReplacedValue = Encoding.UTF8.GetBytes("replaced");
    private static readonly byte[] SecondValue = Encoding.UTF8.GetBytes("second");
    private const long OneCommit = 1;

    [Test]
    public Task AcRep008ResetDiscardsOnlyTheCurrentActionAndEveryActionCommitsInOneTransaction()
        => PreparationOwnershipOwner.RunAsync(async owner =>
        {
            var position = owner.Store.Position;
            var publications = owner.Publication.Count;
            var observed = owner.Store.Commit((transaction, _) =>
            {
                var staging = (IAtomicActionStaging)transaction;
                var firstInput = FirstKey.ToArray();
                var firstValueInput = FirstValue.ToArray();
                transaction.Put(firstInput, firstValueInput);
                Array.Clear(firstInput);
                Array.Clear(firstValueInput);
                var captured = staging.CaptureWrites();
                staging.BeginAction();
                transaction.Put(DiscardedKey, SecondValue);
                var replacementInput = FirstKey.ToArray();
                transaction.Put(replacementInput, ReplacedValue);
                Array.Clear(replacementInput);
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
                return (AfterReset: afterReset, Child: child, FirstEffect: captured.Single());
            });

            var firstEffect = observed.FirstEffect.BorrowOwnedBuffers();
            await Assert.That(firstEffect.Key).IsEquivalentTo(FirstKey, CollectionOrdering.Matching);
            await Assert.That(firstEffect.Value).IsEquivalentTo(FirstValue, CollectionOrdering.Matching);
            await Assert.That(observed.AfterReset.First).IsEquivalentTo(FirstValue, CollectionOrdering.Matching);
            await Assert.That(observed.AfterReset.Discarded).IsNull();
            await Assert.That(observed.Child.First).IsEquivalentTo(FirstValue, CollectionOrdering.Matching);
            await Assert.That(observed.Child.Second).IsEquivalentTo(SecondValue, CollectionOrdering.Matching);
            await Assert.That(observed.Child.Child).IsNull();
            await Assert.That(owner.Publication.Count).IsEqualTo(publications + OneCommit);
            await Assert.That(owner.Store.Position).IsEqualTo(owner.Publication.Position);
            await Assert.That(owner.Store.Position).IsGreaterThan(position);
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(FirstKey))).IsEquivalentTo(FirstValue, CollectionOrdering.Matching);
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(SecondKey))).IsEquivalentTo(SecondValue, CollectionOrdering.Matching);
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(DiscardedKey))).IsNull();
            await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(ChildKey))).IsNull();
        });
}
