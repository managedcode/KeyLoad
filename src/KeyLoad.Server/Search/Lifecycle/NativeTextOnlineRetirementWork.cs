namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineRetirementWork
{
    internal static async Task RetireAfterReadersAsync(Task start, NativeTextOnlineGeneration original,
        NativeTextOnlineRoot root, Lock generationGate, Dictionary<string, NativeTextOnlineGeneration> generations)
    {
        await start.ConfigureAwait(false);
        await original.MarkRetirement().ConfigureAwait(false);
        original.CompleteAfterJoinedRetirement();
        root.DeleteAfterJoinedOwnership(original.Original.Authority.Leaf, original.Manifest.Scope);
        original.ReleaseAfterActualDirectoryDeletion();
        lock (generationGate)
        {
            generations.Remove(original.Original.Authority.Leaf);
        }
    }

    internal static async Task ObserveCompletedAsync(Lock generationGate, Dictionary<string, Task> retirements)
    {
        KeyValuePair<string, Task>[] originals;
        lock (generationGate)
        {
            originals = retirements.Where(pair => pair.Value.IsCompleted).ToArray();
        }
        var failures = new List<Exception>();
        foreach (var original in originals)
        {
            var before = failures.Count;
            await ServerFailureObserver.ObserveAsync(() => original.Value, failures).ConfigureAwait(false);
            if (failures.Count != before)
            {
                continue;
            }
            lock (generationGate)
            {
                if (retirements.TryGetValue(original.Key, out var actual) && ReferenceEquals(actual, original.Value))
                {
                    retirements.Remove(original.Key);
                }
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
