using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

namespace KeyLoad.Comparisons;

internal static class OpenLoopHealthyReadVerifier
{
    internal static async Task<OpenLoopHealthyReadResult> ReadAndVerifyAsync(IComparisonTarget target,
        ScaledComparisonCorpus corpus, CancellationToken hostToken)
    {
        var expected = corpus.CreateDocument(0);
        var session = await target.OpenSessionAsync(hostToken).ConfigureAwait(false);
        Exception? primary = null;
        OpenLoopCancellationHealthRead? result = null;
        try
        {
            if (session is not IOpenLoopCancellationHealthSession healthSession)
            {
                throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationHealthReadUnavailable);
            }
            result = await healthSession.ReadActualAsync(expected, hostToken).ConfigureAwait(false);
            Verify(result, expected);
        }
        catch (Exception failure)
        {
            primary = failure;
        }
        var cleanup = ImmutableArray.CreateBuilder<Exception>();
        try { await session.DisposeAsync().ConfigureAwait(false); }
        catch (Exception failure) { cleanup.Add(failure); }
        var combined = OpenLoopFailure.Combine(primary, cleanup.ToImmutable());
        if (combined is not null)
        {
            ExceptionDispatchInfo.Capture(combined).Throw();
        }
        var actual = result?.Actual ?? throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationHealthyReadMissing);
        return new(actual.Revision, OpenLoopCancellationProofValidation.HashJson(actual.Json), SessionClosed: true);
    }

    private static void Verify(OpenLoopCancellationHealthRead result, BenchmarkDocument expected)
    {
        var actual = result.Actual;
        if (result.RequestedReference != actual.Reference || actual.Reference.Collection != OpenLoopProtocolIdentities.DocumentsCollection
            || actual.Reference.Id != expected.Id || actual.Revision <= 0 || actual.Redacted
            || !actual.RedactedFields.IsEmpty || !string.Equals(actual.Json, expected.Json, StringComparison.Ordinal))
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationHealthyReadMismatch);
        }
    }
}

internal sealed record OpenLoopHealthyReadResult(long Revision, string JsonSha256, bool SessionClosed);
