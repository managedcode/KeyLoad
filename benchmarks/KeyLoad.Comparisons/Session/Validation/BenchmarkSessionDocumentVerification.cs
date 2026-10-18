namespace KeyLoad.Comparisons;

internal sealed class BenchmarkSessionDocumentVerification(BenchmarkSessionContext context)
{
    internal async Task<DocumentReadbackEvidence> VerifyAsync(bool afterMixed, CancellationToken token)
    {
        var native = (IDocumentComparisonTarget)context.Target;
        await using var reader = await native.OpenDocumentSessionAsync(token).ConfigureAwait(false);
        var schedule = new DocumentComparisonSchedule(afterMixed ? DocumentComparisonScenario.MixedCrud : DocumentComparisonScenario.SequentialRead,
            context.Corpus, afterMixed ? checked(context.Options.Operations * BenchmarkSessionLimits.DocumentMixedLanes) : context.Options.Operations);
        var result = await DocumentComparisonOracle.VerifyAsync(reader.ReadDocumentsAsync(token), schedule.FinalDocuments(), token).ConfigureAwait(false);
        if (result.Records != context.Options.Records)
        {
            throw new ComparisonFailureException(BenchmarkSessionProtocol.SessionFinalCardinalityMismatch);
        }
        await native.VerifyDocumentCopiesAsync(schedule, token).ConfigureAwait(false);
        return result;
    }
}
