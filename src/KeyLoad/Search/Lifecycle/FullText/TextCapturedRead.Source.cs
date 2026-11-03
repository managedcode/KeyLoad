using KeyLoad.Core.DocumentStorage;
using KeyLoad.Core.Search;
using KeyLoad.Query.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Search;

internal sealed partial class TextCapturedRead
{
    public IKeyValueView CompleteSource(TextPostingCandidates? certified = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this) /* KLD0041: BCL disposal diagnostics accept the this TextCapturedRead instance through object to preserve its runtime owner type. */;
        if (captured is not null)
        {
            throw new InvalidOperationException(AlreadySettled);
        }
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => CaptureLookups(certified), failures);
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
        var callback = (TextPostingObservation.Capture()).Value;
        if (callback is not null && OriginalProjection is IOriginalTextPostingObservation observed)
        {
            observed.ObserveOriginalPosting(callback);
        }
        return new SearchCapturedReadScope(captured!, budget);
    }

    private void CaptureLookups(TextPostingCandidates? certified)
    {
        if (certified is not null && request.VectorField is null)
        {
            var prefix = DocumentStorageKeys.Prefix(request.Partition, request.Collection);
            foreach (var candidate in certified.Candidates)
            {
                budget.Check();
                builder.CaptureExact(DocumentStorageKeys.RecordKey(prefix, candidate.Reference.Id));
            }
        }
        SearchLookupClosure.Capture(database, builder, Principal, request.Partition, request.Collection,
            request.VectorField, request.TextField, TextProjectionProtocol.TokenizerVersion, budget,
            allDocuments: certified is null);
    }

    public void VerifyTerminal(IReadOnlyList<EntityRef> selected)
    {
        ObjectDisposedException.ThrowIf(disposed, this) /* KLD0041: BCL disposal diagnostics accept the this TextCapturedRead instance through object to preserve its runtime owner type. */;
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
        runtimeJoined = true;
    }
}
