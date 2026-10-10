using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

/// <summary>Settles the actual native postings and files before exposing a checkpoint intent.</summary>
internal static class NativeTextIncrementalGenerationFlow
{
    internal static NativeTextIncrementalManifest Apply(NativeTextIncrementalNativeOwner actual,
        NativeTextIncrementalIntent intent, NativeTextIncrementalManifest proposed,
        int maximumRecords, int maximumChanges, ReadExecutionBudget budget,
        IOptions<NativeTextExecutionOptions> options)
    {
        NativeTextIncrementalIntentValidation.Require(intent, proposed.Scope, proposed.Consumer,
            proposed.Generation, proposed.Placement, maximumRecords, maximumChanges, budget);
        if (intent.NextRecord != proposed.NextRecord)
        { throw NativeTextErrors.Corrupt(); }
        RequireRecords(intent, proposed, budget);
        NativeTextIncrementalReplayPreparation.Prepare(actual.Root, actual.Leaf, intent, maximumRecords,
            maximumChanges, budget, options, actual.Resources);
        var failures = new List<Exception>();
        NativeTextIncrementalManifest? completed = null;
        try
        {
            ServerFailureObserver.Observe(() =>
            {
                actual.Open(budget);
                actual.ApplyIntent(intent, options.Value.MaximumDiskBytes, budget);
                var files = actual.CloseAndCapture(budget);
                actual.Observe(NativeTextFaultStage.NativeInventoryFlushed);
                completed = proposed with { Files = files };
                NativeTextIncrementalValidation.Manifest(completed, proposed.Scope, proposed.Consumer,
                    proposed.Generation, proposed.Placement, maximumRecords, budget, options);
                NativeTextIncrementalMetadata.Publish(actual.Path, completed,
                    options.Value.MaximumDiskBytes, budget, options, actual.Resources);
                actual.Observe(NativeTextFaultStage.ManifestPublished);
            }, failures);
        }
        finally { ServerFailureObserver.Observe(actual.CloseAfterOperation, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return completed ?? throw NativeTextErrors.Corrupt();
    }

    private static void RequireRecords(NativeTextIncrementalIntent intent,
        NativeTextIncrementalManifest manifest, ReadExecutionBudget budget)
    {
        budget.ChargeBytes(NativeSerialization.Measure(intent.Records));
        budget.ChargeBytes(NativeSerialization.Measure(manifest.Records));
        var original = NativeSerialization.Serialize(intent.Records);
        var target = NativeSerialization.Serialize(manifest.Records);
        budget.Check();
        if (!original.AsSpan().SequenceEqual(target))
        { throw NativeTextErrors.Corrupt(); }
    }
}
