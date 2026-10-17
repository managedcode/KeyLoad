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
            {
                throw NativeTextErrors.Ownership();
            }
            retirements.Add(leaf, NativeTextOnlineRetirementWork.RetireAfterReadersAsync(start.Task, original, root, generationGate, generations));
        }
        start.TrySetResult();
    }

    internal Task ObserveCompletedRetirementsAsync()
        => NativeTextOnlineRetirementWork.ObserveCompletedAsync(generationGate, retirements);

    internal Task[] CaptureOriginalRetirements()
    {
        lock (generationGate)
        {
            return retirements.Values.ToArray();
        }
    }

    internal NativeTextOnlineGeneration[] CaptureGenerations()
    {
        lock (generationGate)
        {
            return generations.Values.ToArray();
        }
    }
}
