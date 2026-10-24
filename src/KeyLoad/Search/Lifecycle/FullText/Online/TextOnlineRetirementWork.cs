namespace KeyLoad.Server.Search;

internal static class TextOnlineRetirementWork
{
    internal static async Task RetireAfterReadersAsync(Task start, TextOnlineGeneration original,
        TextOnlineRoot root, Lock generationGate, Dictionary<string, TextOnlineGeneration> generations)
    {
        await start;
        await original.MarkRetirement();
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
            await ServerFailureObserver.ObserveAsync(() => original.Value, failures);
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
