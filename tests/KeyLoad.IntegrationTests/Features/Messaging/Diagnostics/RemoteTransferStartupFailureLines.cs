namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Preserves actual exception/stack identity without messages, Data or private request bytes.</summary>
internal static class RemoteTransferStartupFailureLines
{
    private const string MissingStack = "The original exception stack is unavailable.";

    internal static IEnumerable<string> Read(Exception original)
    {
        yield return $"ActualInitiatingExceptionType={original.GetType().FullName}; NativeCode={(original as KeyLoadException)?.Code}";
        using var stack = new StringReader(original.StackTrace ?? MissingStack);
        while (stack.ReadLine() is { } line)
        { yield return line; }
        if (original is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            { foreach (var line in Read(inner)) { yield return line; } }
        }
        else if (original.InnerException is { } inner)
        { foreach (var line in Read(inner)) { yield return line; } }
    }
}
