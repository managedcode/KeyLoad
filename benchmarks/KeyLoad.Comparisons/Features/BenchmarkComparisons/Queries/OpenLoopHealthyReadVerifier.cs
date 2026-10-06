using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

namespace KeyLoad.Comparisons;

internal static class OpenLoopHealthyReadVerifier
{
    private const int FirstCorpusDocumentIndex = 0;
    internal static async Task<OpenLoopHealthyReadResult> ReadAndVerifyAsync(IComparisonTarget target,
        ScaledComparisonCorpus corpus, CancellationToken hostToken)
    {
        var expected = corpus.CreateDocument(FirstCorpusDocumentIndex);
        var session = await target.OpenSessionAsync(hostToken).ConfigureAwait(false);
        var result = await SettleReadAsync(ReadSessionAsync(session, expected, hostToken), session.DisposeAsync)
            .ConfigureAwait(false);
        var actual = result.Actual;
        return new(actual.Revision, OpenLoopCancellationProofValidation.HashJson(actual.Json), SessionClosed: true);
    }

    internal static async Task<OpenLoopCancellationHealthRead> SettleReadAsync(
        Task<OpenLoopCancellationHealthRead> original, Func<ValueTask> dispose)
    {
        Exception? primary = null;
        OpenLoopCancellationHealthRead? result = null;
        try
        {
            result = await original.ConfigureAwait(false);
        }
        catch (Exception failure) when (original.IsFaulted || original.IsCanceled)
        {
            primary = failure;
        }
        var cleanup = ImmutableArray.CreateBuilder<Exception>();
        var disposalFailure = await OpenLoopFailure.ObserveAsync(DisposeSessionAsync(dispose)).ConfigureAwait(false);
        if (disposalFailure is not null)
        {
            cleanup.Add(disposalFailure);
        }
        var combined = OpenLoopFailure.Combine(primary, cleanup.ToImmutable());
        if (combined is not null)
        {
            ExceptionDispatchInfo.Capture(combined).Throw();
        }
        return result ?? throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationHealthyReadMissing);
    }

    private static async Task<OpenLoopCancellationHealthRead> ReadSessionAsync(IComparisonSession session,
        BenchmarkDocument expected, CancellationToken cancellationToken)
    {
        if (session is not IOpenLoopCancellationHealthSession healthSession)
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationHealthReadUnavailable);
        }
        var result = await healthSession.ReadActualAsync(expected, cancellationToken).ConfigureAwait(false);
        Verify(result, expected);
        return result;
    }

    private static async Task DisposeSessionAsync(Func<ValueTask> dispose)
    {
        await dispose().ConfigureAwait(false);
    }

    private static void Verify(OpenLoopCancellationHealthRead result, BenchmarkDocument expected)
    {
        const int BeforeFirstRevision = 0;

        var actual = result.Actual;
        if (result.RequestedReference != actual.Reference || actual.Reference.Collection != OpenLoopProtocolIdentities.DocumentsCollection
            || actual.Reference.Id != expected.Id || actual.Revision <= BeforeFirstRevision || actual.Redacted
            || !actual.RedactedFields.IsEmpty || !string.Equals(actual.Json, expected.Json, StringComparison.Ordinal))
        {
            throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationHealthyReadMismatch);
        }
    }
}

internal sealed record OpenLoopHealthyReadResult(long Revision, string JsonSha256, bool SessionClosed);
