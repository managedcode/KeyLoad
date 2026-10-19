using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.Search;

internal sealed partial class TextCapturedRead
{
    public IKeyValueView CompleteSource()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (captured is not null)
        {
            throw new InvalidOperationException(AlreadySettled);
        }
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => SearchLookupClosure.Capture(database, builder, Principal,
            request.Partition, request.Collection, request.VectorField, request.TextField, TextProjectionProtocol.TokenizerVersion, budget), failures);
        if (failures.Count == NoFailures)
        {
            ServerFailureObserver.Observe(RuntimeJoin, failures);
        }
        if (failures.Count == NoFailures)
        {
            ServerFailureObserver.Observe(() => captured = builder.Transfer(), failures);
        }
        if (failures.Count != NoFailures)
        {
            ServerFailureObserver.Observe(Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        budget.Check();
        var callback = TextPostingObservation.Capture();
        if (callback is not null && OriginalProjection is IOriginalTextPostingObservation observed)
        {
            observed.ObserveOriginalPosting(callback);
        }
        return new SearchCapturedReadScope(captured!, budget);
    }

    public void VerifyTerminal(IReadOnlyList<EntityRef> selected)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!runtimeJoined || captured is null)
        {
            throw new InvalidOperationException(NotSettled);
        }
        SearchTerminalPrivacy.Verify(database, Principal.Id, request.Partition, request.Collection,
            request.TextField!, request.VectorField, authority, captured, selected, budget);
    }

    private void RuntimeJoin()
    {
        if (runtimeJoined)
        {
            return;
        }
        execution.Dispose();
        budget.CompleteSettledReadGrant(grant);
        runtimeJoined = true;
    }
}
