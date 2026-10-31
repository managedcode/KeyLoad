using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

/// <summary>Settles the actual execution postings and files before exposing a checkpoint intent.</summary>
internal static class TextIncrementalGenerationFlow
{
    internal static TextIncrementalManifest Apply(TextIncrementalOwner actual,
        TextIncrementalIntent intent, TextIncrementalManifest proposed, ReadExecutionBudget budget,
        IOptions<TextExecutionOptions> options)
    {
        TextIncrementalIntentValidation.Require(intent, proposed.Scope, proposed.Consumer,
            proposed.Generation, proposed.Placement, budget);
        if (intent.NextRecord != proposed.NextRecord)
        {
            throw TextErrors.Corrupt();
        }
        RequireRecords(intent, proposed, budget);
        TextIncrementalReplayPreparation.Prepare(actual.Root, actual.Leaf, intent, budget, options, actual.Resources);
        var failures = new List<Exception>();
        TextIncrementalManifest? completed = null;
        try
        {
            ServerFailureObserver.Observe(() =>
            {
                actual.Open(budget);
                actual.ApplyIntent(intent, options.Value.MaximumDiskBytes, budget);
                var files = actual.CloseAndCapture(budget);
                actual.Observe(TextFaultStage.InventoryFlushed);
                completed = proposed with
                {
                    Files = files
                };
                TextIncrementalValidation.Manifest(completed, proposed.Scope, proposed.Consumer,
                    proposed.Generation, proposed.Placement, budget, options);
                TextIncrementalMetadata.Publish(actual.Path, completed,
                    options.Value.MaximumDiskBytes, budget, options, actual.Resources);
                actual.Observe(TextFaultStage.ManifestPublished);
            }, failures);
        }
        finally { ServerFailureObserver.Observe(actual.CloseAfterOperation, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return completed ?? throw TextErrors.Corrupt();
    }

    private static void RequireRecords(TextIncrementalIntent intent,
        TextIncrementalManifest manifest, ReadExecutionBudget budget)
    {
        var original = OrleansSerialization.Serialize(intent.Records);
        var target = OrleansSerialization.Serialize(manifest.Records);
        budget.Check();
        if (!original.AsSpan().SequenceEqual(target))
        {
            throw TextErrors.Corrupt();
        }
    }
}
