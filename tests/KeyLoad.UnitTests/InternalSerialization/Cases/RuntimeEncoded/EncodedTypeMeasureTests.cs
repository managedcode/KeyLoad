namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class EncodedTypeMeasureTests
{
    private const int ConcurrentOperations = 20;
    private const int FirstOperation = 0;

    [Test]
    public async Task RegisteredTypeFramesResolveBeforeCompleteLiteralPayloadRoundtrip()
    {
        await EncodedTypeFrameFixture.RequireAsync(EncodedTypeFrameFixture.Command());
        await EncodedTypeFrameFixture.RequireAsync(EncodedTypeFrameFixture.Counters());
    }

    [Test]
    public async Task MalformedTypeVersionRejectsBeforeFreshHealthyFrameAndCompletePayload()
    {
        var original = EncodedTypeFrameFixture.Encode<CommandRequest>();
        var malformed = original.ToArray();
        malformed[EncodedTypeFrameFixture.FrameVersionIndex] = EncodedTypeFrameFixture.UnsupportedVersion;
        _ = Assert.ThrowsExactly<NotSupportedException>(() =>
            EncodedTypeFrameFixture.Decode<CommandRequest>(malformed));
        await Assert.That(EncodedTypeFrameFixture.Encode<CommandRequest>())
            .IsEquivalentTo(original, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await EncodedTypeFrameFixture.RequireAsync(EncodedTypeFrameFixture.Command());
    }

    [Test]
    public async Task ConcurrentFreshSessionsJoinAndCancelledAdmissionHasHealthyContinuation()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var admitted = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Parallel.ForEachAsync(Enumerable.Range(FirstOperation, ConcurrentOperations),
            new ParallelOptions { MaxDegreeOfParallelism = ConcurrentOperations, CancellationToken = token },
            async (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref admitted) == ConcurrentOperations)
                {
                    started.SetResult();
                }
                await started.Task.WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await EncodedTypeFrameFixture.RequireAsync(EncodedTypeFrameFixture.Command());
                await EncodedTypeFrameFixture.RequireAsync(EncodedTypeFrameFixture.Counters());
            });
        await Assert.That(admitted).IsEqualTo(ConcurrentOperations);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cancelledAdmissions = 0;
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Parallel.ForEachAsync(Enumerable.Range(FirstOperation, ConcurrentOperations),
                new ParallelOptions { MaxDegreeOfParallelism = ConcurrentOperations, CancellationToken = cancelled.Token },
                (_, _) =>
                {
                    Interlocked.Increment(ref cancelledAdmissions);
                    return ValueTask.CompletedTask;
                }));
        await Assert.That(cancelledAdmissions).IsEqualTo(FirstOperation);
        await Assert.That(failure!.CancellationToken).IsEqualTo(cancelled.Token);
        await EncodedTypeFrameFixture.RequireAsync(EncodedTypeFrameFixture.Command());
        await EncodedTypeFrameFixture.RequireAsync(EncodedTypeFrameFixture.Counters());
    }
}
