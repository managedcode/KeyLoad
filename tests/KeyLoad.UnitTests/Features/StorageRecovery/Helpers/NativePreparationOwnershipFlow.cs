using KeyLoad.Server;
using KeyLoad.Storage;
using A = KeyLoad.UnitTests.Features.StorageRecovery.NativePreparationOwnershipAssertions;
using O = KeyLoad.UnitTests.Features.StorageRecovery.NativePreparationOwnershipOwner;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class NativePreparationOwnershipFlow
{
    private const string ClosedChild = "The native preparation transaction is outside its synchronous owning callback.";
    private const string ClosedParent = "Transaction preparation requires its original synchronous compiler callback.";
    private const string NestedStore = "Store preparation cannot enter an already owned native gate.";

    internal static async Task DiscardAsync(O owner)
    {
        var position = owner.Store.Position;
        var journal = owner.Journal;
        var image = A.Image(owner);
        var result = owner.Store.Prepare((child, proposed) =>
        {
            child.Put(O.ChildKey, O.ChildValue);
            child.Delete(O.SeedKey);
            return (Position: proposed, Value: child.ReadOwnedValue(O.ChildKey), Absent: child.ReadOwnedValue(O.SeedKey));
        });
        await Assert.That(result.Position).IsEqualTo(checked(position + 1));
        await A.BytesAsync(O.ChildValue, result.Value);
        await Assert.That(result.Absent).IsNull();
        await A.ConservedAsync(owner, position, journal, image);
        var original = new InvalidOperationException("native-prepare-original-failure");
        var observed = Assert.ThrowsExactly<InvalidOperationException>(() => owner.Store.Prepare<bool>((child, _) =>
        {
            child.Put(O.ChildKey, O.ChildValue);
            throw original;
        }));
        await Assert.That(ReferenceEquals(original, observed)).IsTrue();
        await A.ConservedAsync(owner, position, journal, image);
        await A.HealthyAsync(owner);
    }

    internal static async Task InheritedAsync(O owner)
    {
        var position = owner.Store.Position;
        byte[]? inherited = null;
        byte[]? resetParent = null;
        byte[]? resetSeed = null;
        byte[]? resetChild = null;
        byte[]? parentAfter = null;
        byte[]? parentSeed = null;
        byte[]? parentChild = null;
        owner.Store.Commit((parent, _) =>
        {
            parent.Put(O.ParentKey, O.ParentValue);
            parent.Delete(O.SeedKey);
            parent.Prepare(child =>
            {
                inherited = child.ReadOwnedValue(O.ParentKey);
                child.Put(O.ParentKey, O.ChildValue);
                child.Put(O.SeedKey, O.ChildValue);
                child.Put(O.ChildKey, O.ChildValue);
                child.Reset();
                resetParent = child.ReadOwnedValue(O.ParentKey);
                resetSeed = child.ReadOwnedValue(O.SeedKey);
                resetChild = child.ReadOwnedValue(O.ChildKey);
                return true;
            });
            parentAfter = parent.ReadOwnedValue(O.ParentKey);
            parentSeed = parent.ReadOwnedValue(O.SeedKey);
            parentChild = parent.ReadOwnedValue(O.ChildKey);
            return true;
        });
        await A.BytesAsync(O.ParentValue, inherited);
        await A.BytesAsync(O.ParentValue, resetParent);
        await A.BytesAsync(O.ParentValue, parentAfter);
        await Assert.That(resetSeed).IsNull();
        await Assert.That(resetChild).IsNull();
        await Assert.That(parentSeed).IsNull();
        await Assert.That(parentChild).IsNull();
        await Assert.That(owner.Store.Position).IsEqualTo(checked(position + 1));
        await A.BytesAsync(O.ParentValue, owner.Store.Read(view => view.ReadOwnedValue(O.ParentKey)));
        await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(O.SeedKey))).IsNull();
        await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(O.ChildKey))).IsNull();
    }

    internal static async Task LifetimeAsync(O owner)
    {
        var position = owner.Store.Position;
        var journal = owner.Journal;
        var image = A.Image(owner);
        IAtomicTransaction? escapedChild = null;
        IAtomicTransaction? escapedParent = null;
        var callbacks = 0;
        var failures = new List<Exception>();
        var childThread = new NativePreparationOwnershipThread();
        ServerFailureObserver.Observe(() => owner.Store.Prepare((child, proposed) =>
        {
            escapedChild = child;
            childThread.StartAndObserve(() => child.Prepare(_ => ++callbacks), ClosedChild);
            return true;
        }), failures);
        childThread.JoinAfterCallback(failures);
        ServerFailureObserver.ThrowIfAny(failures);
        if (escapedChild is null)
        {
            throw new InvalidOperationException("The real native preparation callback was not reached.");
        }
        A.Refuse(() => escapedChild.Put(O.ChildKey, O.ChildValue), ClosedChild);
        A.Refuse(() => escapedChild.ReadOwnedValue(O.SeedKey), ClosedChild);
        A.Refuse(() => escapedChild.Prepare(_ => ++callbacks), ClosedChild);
        var parentThread = new NativePreparationOwnershipThread();
        ServerFailureObserver.Observe(() => owner.Store.Commit((parent, proposed) =>
        {
            escapedParent = parent;
            parentThread.StartAndObserve(() => parent.Prepare(_ => ++callbacks), ClosedParent);
            return true;
        }), failures);
        parentThread.JoinAfterCallback(failures);
        ServerFailureObserver.ThrowIfAny(failures);
        if (escapedParent is null)
        {
            throw new InvalidOperationException("The real native compiler callback was not reached.");
        }
        A.Refuse(() => escapedParent.Prepare(_ => ++callbacks), ClosedParent);
        await Task.Run(() => A.Refuse(() => escapedChild.Prepare(_ => ++callbacks), ClosedChild));
        await Task.Run(() => A.Refuse(() => escapedParent.Prepare(_ => ++callbacks), ClosedParent));
        await Assert.That(callbacks).IsEqualTo(0);
        await A.ConservedAsync(owner, position, journal, image);
        await A.HealthyAsync(owner);
    }

    internal static async Task NestedAsync(O owner)
    {
        var position = owner.Store.Position;
        var journal = owner.Journal;
        var image = A.Image(owner);
        var callbacks = 0;
        owner.Store.Read(view =>
        {
            A.Refuse(() => owner.Store.Prepare((_, _) => ++callbacks), NestedStore);
            return true;
        });
        owner.Store.Commit((_, _) =>
        {
            A.Refuse(() => owner.Store.Prepare((_, _) => ++callbacks), NestedStore);
            return true;
        });
        await Assert.That(callbacks).IsEqualTo(0);
        await A.ConservedAsync(owner, position, journal, image);
        await A.HealthyAsync(owner);
    }
}
