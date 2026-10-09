namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Bounded original task witnesses and one common admitted-client start barrier.</summary>
internal sealed class HeavyDocumentLoadOverlap
{
    private readonly Task?[] originals = new Task?[HeavyDocumentLoadProtocol.Writers];
    private readonly TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int ready;
    private int completedWriters;
    private long acknowledged;
    private readonly int[] overlappingReads = new int[HeavyDocumentLoadProtocol.ReaderLanes];

    internal bool WritersDone => Volatile.Read(ref completedWriters) == HeavyDocumentLoadProtocol.Writers;
    internal long Acknowledged => Interlocked.Read(ref acknowledged);
    internal async Task ArriveAsync(CancellationToken token)
    {
        if (Interlocked.Increment(ref ready) == HeavyDocumentLoadProtocol.Writers + HeavyDocumentLoadProtocol.ReaderLanes)
        { start.SetResult(); }
        await start.Task.WaitAsync(token).ConfigureAwait(false);
    }
    internal void Original(int writer, Task? original) => Volatile.Write(ref originals[writer], original);
    internal void Acknowledge() => Interlocked.Increment(ref acknowledged);
    internal void CompleteWriter() => Interlocked.Increment(ref completedWriters);
    internal bool HasIncompleteOriginal()
    {
        for (var index = 0; index < originals.Length; index++)
        {
            if (Volatile.Read(ref originals[index]) is { IsCompleted: false })
            { return true; }
        }
        return false;
    }
    internal void ReadOverlapped(int lane) => Interlocked.Increment(ref overlappingReads[lane]);
    internal async Task VerifyAsync()
    {
        await Assert.That(Acknowledged).IsEqualTo((long)HeavyDocumentLoadProtocol.Records);
        await Assert.That(WritersDone).IsTrue();
        foreach (var count in overlappingReads)
        { await Assert.That(count).IsGreaterThan(0); }
    }
}
