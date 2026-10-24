namespace KeyLoad.Server.Search;

internal sealed partial class TextOnlineCatalogOwner
{
    private readonly Dictionary<string, Task> retirements = new(StringComparer.Ordinal);

    internal void BeginRetirement(TextOnlineGeneration original)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (generationGate)
        {
            var leaf = original.Original.Authority.Leaf;
            if (retirements.ContainsKey(leaf))
            {
                throw TextErrors.Ownership();
            }
            retirements.Add(leaf, TextOnlineRetirementWork.RetireAfterReadersAsync(start.Task, original, root, generationGate, generations));
        }
        start.TrySetResult();
    }

    internal Task ObserveCompletedRetirementsAsync()
        => TextOnlineRetirementWork.ObserveCompletedAsync(generationGate, retirements);

    internal Task[] CaptureOriginalRetirements()
    {
        lock (generationGate)
        {
            return retirements.Values.ToArray();
        }
    }

    internal TextOnlineGeneration[] CaptureGenerations()
    {
        lock (generationGate)
        {
            return generations.Values.ToArray();
        }
    }
}
