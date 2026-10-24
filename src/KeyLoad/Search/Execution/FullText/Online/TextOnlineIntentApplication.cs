using KeyLoad.Core;

namespace KeyLoad.Server.Search;

internal static class TextOnlineIntentApplication
{
    internal static void Execute(DatabaseEngine database, TextOnlineSession session,
        TextOnlineRoot root, TextResourceOwnership resources, ServerRuntimeOptions options)
    {
        var budget = session.Budget;
        root.Verify(budget);
        var actual = TextOnlinePagePreparation.RequireOwner(session);
        var intent = session.Intent ?? throw TextErrors.Ownership();
        var target = session.Target ?? throw TextErrors.Ownership();
        TextIncrementalIntentValidation.Require(intent, target.Scope, target.Consumer,
            target.Generation, target.Placement, database.Limits.MaxScanRecords,
            database.Limits.MaxScanRecords, budget);
        budget.ChargeBytes(OrleansSerialization.Measure(intent.Records));
        budget.ChargeBytes(OrleansSerialization.Measure(target.Records));
        if (intent.NextRecord != target.NextRecord || !OrleansSerialization.Serialize(intent.Records).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(target.Records)))
        {
            throw TextErrors.Corrupt();
        }
        var failures = new List<Exception>();
        TextIncrementalManifest? completed = null;
        ServerFailureObserver.Observe(() =>
        {
            actual.Open(budget);
            actual.ApplyIntent(intent, options.RuntimeText.Value.MaximumDiskBytes, budget);
            completed = target with
            {
                Files = actual.CloseAndCapture(budget)
            };
            TextIncrementalValidation.Manifest(completed, target.Scope, target.Consumer,
                target.Generation, target.Placement, database.Limits.MaxScanRecords, budget, options.RuntimeText);
            TextIncrementalMetadata.Publish(actual.Path, completed,
                options.RuntimeText.Value.MaximumDiskBytes, budget, options.RuntimeText, resources);
        }, failures);
        ServerFailureObserver.Observe(actual.CloseAfterOperation, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        session.Manifest = completed ?? throw TextErrors.Corrupt();
    }
}
