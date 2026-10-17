namespace KeyLoad.CrashHost;

internal static class ExistingStoreInspectorFailures
{
    private const string FailureGraphTooLarge = "The original store failure graph exceeds its bounded protocol.";
    private const string MissingTypeIdentity = "The original store failure has no safe type identity.";

    internal static (string[] Types, string? Code) Collect(List<Exception> originals)
    {
        const int OriginalsCountStep = 1;
        const int IndexValidationBoundary = 0;

        if (originals.Count > ExistingStoreInspectorProtocol.MaximumFailureTypes)
        {
            throw new InvalidDataException(FailureGraphTooLarge);
        }
        var types = new List<string>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Exception>();
        for (var index = originals.Count - OriginalsCountStep; index >= IndexValidationBoundary; index--)
        {
            pending.Push(originals[index]);
        }
        string? code = null;
        var capturedCode = false;
        while (pending.TryPop(out var original))
        {
            if (!visited.Add(original))
            {
                continue;
            }
            if (visited.Count > ExistingStoreInspectorProtocol.MaximumFailureTypes)
            {
                throw new InvalidDataException(FailureGraphTooLarge);
            }
            types.Add(original.GetType().FullName ?? throw new InvalidDataException(MissingTypeIdentity));
            if (!capturedCode && original is KeyLoadException keyLoad)
            {
                capturedCode = true;
                code = Enum.GetName(keyLoad.Code);
            }
            PushCauses(original, pending);
        }
        return ([.. types], code);
    }

    private static void PushCauses(Exception original, Stack<Exception> pending)
    {
        const int InnerExceptionsCountStep = 1;
        const int IndexValidationBoundary = 0;

        if (original is AggregateException aggregate)
        {
            if (pending.Count + aggregate.InnerExceptions.Count > ExistingStoreInspectorProtocol.MaximumFailureTypes)
            {
                throw new InvalidDataException(FailureGraphTooLarge);
            }
            for (var index = aggregate.InnerExceptions.Count - InnerExceptionsCountStep; index >= IndexValidationBoundary; index--)
            {
                pending.Push(aggregate.InnerExceptions[index]);
            }
        }
        else if (original.InnerException is { } inner)
        {
            pending.Push(inner);
        }
    }
}
