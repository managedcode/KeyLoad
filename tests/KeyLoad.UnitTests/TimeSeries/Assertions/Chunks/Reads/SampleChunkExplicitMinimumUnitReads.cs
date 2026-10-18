using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkExplicitMinimumUnitReads
{
    private const long NoPosition = 0;
    private const long NegativePosition = -1;
    private const string OtherPartition = "foreign-chunk-minimum";
    private const string WrongIncarnation = "The document session token belongs to another incarnation.";
    private const string WrongScope = "The document session token belongs to another atomic partition.";
    private const string FuturePosition = "The document session token is beyond the current committed storage cut.";
    private const string InvalidPosition = "The document session token position must be positive.";
    internal const string ScopeDenied = "The principal cannot perform this operation in this scope.";

    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store,
        ReadSampleChunkWindowRequest request, CommitToken minimum, string principal, CancellationToken token)
    {
        await ObserveAsync(database, store, request, principal, () => Require(database, request, principal, token), token);
        await ObserveAsync(database, store, request, principal,
            () => Require(database, request with { MinimumToken = minimum }, principal, token), token);
        var trials = new (CommitToken Token, string Detail)[]
        {
            (minimum with { Incarnation = Guid.Empty }, WrongIncarnation),
            (minimum with { AtomicPartitionId = OtherPartition }, WrongScope),
            (minimum with { Position = long.MaxValue }, FuturePosition),
            (minimum with { Position = NoPosition }, InvalidPosition),
            (minimum with { Position = NegativePosition }, InvalidPosition)
        };
        foreach (var trial in trials)
        {
            await ObserveAsync(database, store, request, principal, () =>
            {
                var failed = Assert.ThrowsExactly<KeyLoadException>(() => database.ReadSampleChunkWindow(
                    principal, request with
                    {
                        MinimumToken = trial.Token
                    }, token));
                RequireBytes(Errors.Problem(ErrorCode.TokenInvalidated, trial.Detail), failed.ToProblem());
            }, token);
        }
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await ObserveAsync(database, store, request, principal, () =>
        {
            var error = Assert.ThrowsExactly<OperationCanceledException>(() => database.ReadSampleChunkWindow(
                principal, request with
                {
                    MinimumToken = minimum
                }, canceled.Token));
            if (error.CancellationToken != canceled.Token)
            {
                throw new InvalidOperationException("The chunk refusal lost the original cancellation token.");
            }
        }, token);
    }

    internal static async Task PermissionAsync(DatabaseEngine database, ZoneTreeStore store,
        ReadSampleChunkWindowRequest request, string principal, CancellationToken token)
    {
        await ObserveAsync(database, store, request with
        {
            MinimumToken = null
        }, SampleChunkCanonicalFixture.Principal, () =>
        {
            var failed = Assert.ThrowsExactly<KeyLoadException>(() => database.ReadSampleChunkWindow(principal, request, token));
            RequireBytes(Errors.Problem(ErrorCode.PermissionDenied, ScopeDenied), failed.ToProblem());
        }, token);
    }

    internal static async Task ObserveAsync(DatabaseEngine database, ZoneTreeStore store,
        ReadSampleChunkWindowRequest request, string principal, Action primary, CancellationToken token)
    {
        var cut = SampleRawIntegrityAssertions.Capture(store);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(primary, failures);
        await ServerFailureObserver.ObserveAsync(() => SampleRawIntegrityAssertions.UnchangedAsync(store, cut), failures);
        ServerFailureObserver.Observe(() => Require(database, request, principal, token), failures);
        await ServerFailureObserver.ObserveAsync(() => SampleRawIntegrityAssertions.UnchangedAsync(store, cut), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void Require(DatabaseEngine database, ReadSampleChunkWindowRequest request,
        string principal, CancellationToken token)
    {
        var actual = database.ReadSampleChunkWindow(principal, request, token);
        if (actual.CutPosition <= NoPosition)
        {
            throw new InvalidOperationException("The genuine chunk read has no native cut.");
        }
        var expected = new SampleChunkWindowResult(request.WindowId, SampleChunkCanonicalFixture.Start,
            SampleChunkCanonicalFixture.Until, SampleChunkCanonicalFixture.FirstGeneration,
            SampleChunkCanonicalFixture.SealedRevision, SampleChunkCanonicalFixture.InitialSequence, null,
            [.. SampleChunkCanonicalFixture.Expected()], actual.CutPosition);
        RequireBytes(expected, actual);
    }

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));

    internal static void RequireBytes<T>(T expected, T actual)
    {
        if (!JsonDefaults.Serialize(expected).AsSpan().SequenceEqual(JsonDefaults.Serialize(actual)))
        {
            throw new InvalidOperationException("The complete independently authored chunk operation result differs.");
        }
    }
}
