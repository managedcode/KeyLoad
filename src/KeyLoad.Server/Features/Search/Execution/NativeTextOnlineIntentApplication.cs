using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineIntentApplication
{
    internal static void Execute(DatabaseEngine database, NativeTextOnlineSession session,
        NativeTextOnlineRoot root, NativeTextResourceOwnership resources, ServerRuntimeOptions options)
    {
        var budget = session.Budget;
        root.Verify(budget);
        var actual = NativeTextOnlinePagePreparation.RequireOwner(session);
        var intent = session.Intent ?? throw NativeTextErrors.Ownership();
        var target = session.Target ?? throw NativeTextErrors.Ownership();
        NativeTextIncrementalIntentValidation.Require(intent, target.Scope, target.Consumer,
            target.Generation, target.Placement, database.Limits.MaxScanRecords,
            database.Limits.MaxScanRecords, budget);
        budget.ChargeBytes(NativeSerialization.Measure(intent.Records));
        budget.ChargeBytes(NativeSerialization.Measure(target.Records));
        if (intent.NextRecord != target.NextRecord || !NativeSerialization.Serialize(intent.Records).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(target.Records)))
        { throw NativeTextErrors.Corrupt(); }
        var failures = new List<Exception>();
        NativeTextIncrementalManifest? completed = null;
        ServerFailureObserver.Observe(() =>
        {
            actual.Open(budget);
            actual.ApplyIntent(intent, options.NativeText.Value.MaximumDiskBytes, budget);
            completed = target with { Files = actual.CloseAndCapture(budget) };
            NativeTextIncrementalValidation.Manifest(completed, target.Scope, target.Consumer,
                target.Generation, target.Placement, database.Limits.MaxScanRecords, budget, options.NativeText);
            NativeTextIncrementalMetadata.Publish(actual.Path, completed,
                options.NativeText.Value.MaximumDiskBytes, budget, options.NativeText, resources);
        }, failures);
        ServerFailureObserver.Observe(actual.CloseAfterOperation, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        session.Manifest = completed ?? throw NativeTextErrors.Corrupt();
    }
}
