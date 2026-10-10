namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextOnlineCatalogOwner
{
    private readonly Dictionary<string, Task> retirements = new(StringComparer.Ordinal);

    internal void BeginRetirement(NativeTextOnlineGeneration original)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (generationGate)
        {
            var leaf = original.Original.Authority.Leaf;
            if (retirements.ContainsKey(leaf))
            { throw NativeTextErrors.Ownership(); }
            retirements.Add(leaf, RetireAfterReadersAsync(start.Task, original));
        }
        start.TrySetResult();
    }

    private async Task RetireAfterReadersAsync(Task start, NativeTextOnlineGeneration original)
    {
        await start.ConfigureAwait(false);
        await original.MarkRetirement().ConfigureAwait(false);
        original.CompleteAfterJoinedRetirement();
        root.DeleteAfterJoinedOwnership(original.Original.Authority.Leaf, original.Manifest.Scope);
        original.ReleaseAfterActualDirectoryDeletion();
        lock (generationGate)
        { generations.Remove(original.Original.Authority.Leaf); }
    }

    internal async Task ObserveCompletedRetirementsAsync()
    {
        KeyValuePair<string, Task>[] originals;
        lock (generationGate)
        { originals = retirements.Where(pair => pair.Value.IsCompleted).ToArray(); }
        var failures = new List<Exception>();
        foreach (var original in originals)
        {
            var before = failures.Count;
            await ServerFailureObserver.ObserveAsync(() => original.Value, failures).ConfigureAwait(false);
            if (failures.Count != before)
            { continue; }
            lock (generationGate)
            {
                if (retirements.TryGetValue(original.Key, out var actual) && ReferenceEquals(actual, original.Value))
                { retirements.Remove(original.Key); }
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal Task[] CaptureOriginalRetirements()
    {
        lock (generationGate)
        { return retirements.Values.ToArray(); }
    }

    internal NativeTextOnlineGeneration[] CaptureGenerations()
    { lock (generationGate) { return generations.Values.ToArray(); } }
}
