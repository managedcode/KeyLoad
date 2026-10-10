using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextCapturedRead
{
    public IKeyValueView CompleteSource()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (captured is not null)
        { throw new InvalidOperationException(AlreadySettled); }
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => NativeSearchLookupClosure.Capture(database, builder, Principal,
            request.Partition, request.Collection, request.VectorField, budget), failures);
        if (failures.Count == NoFailures)
        { ServerFailureObserver.Observe(JoinNative, failures); }
        if (failures.Count == NoFailures)
        { ServerFailureObserver.Observe(() => captured = builder.Transfer(), failures); }
        if (failures.Count != NoFailures)
        { ServerFailureObserver.Observe(Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        budget.Check();
        var callback = NativeTextPostingObservation.Capture();
        if (callback is not null && OriginalProjection is IOriginalTextPostingObservation observed)
        { observed.ObserveOriginalPosting(callback); }
        return captured!;
    }

    public void VerifyTerminal(IReadOnlyList<EntityRef> selected)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!nativeJoined || captured is null)
        { throw new InvalidOperationException(NotSettled); }
        NativeSearchTerminalPrivacy.Verify(database, Principal.Id, request.Partition, request.Collection,
            request.TextField!, request.VectorField, authority, captured, selected, budget);
    }

    private void JoinNative()
    {
        if (nativeJoined)
        { return; }
        native.Dispose();
        budget.CompleteSettledReadGrant(grant);
        nativeJoined = true;
    }
}
