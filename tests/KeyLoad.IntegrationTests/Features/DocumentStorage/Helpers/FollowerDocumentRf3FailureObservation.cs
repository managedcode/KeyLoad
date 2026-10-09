namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Captures the actual initiating cancellation before the lifecycle owner joins its canceled task.</summary>
internal static class FollowerDocumentRf3FailureObservation
{
    private const int MaximumStackFrames = 16;
    private const int FirstStackFrame = 0;
    private const string Prefix = "follower-original-failure";

    internal static void Write(Exception original, FollowerDocumentRf3State state, CancellationToken parent)
    {
        try
        {
            Console.Error.WriteLine($"{Prefix} stage={state.Stage} caller={state.Mode} change={state.Change} "
                + $"parentCancelled={parent.IsCancellationRequested} waveCancelled={state.WaveLifetime?.IsCancellationRequested} "
                + $"callerCancelled={state.CallLifetime?.IsCancellationRequested} exceptionType={original.GetType().FullName}");
            using var frames = new StringReader(original.StackTrace ?? string.Empty);
            for (var frame = FirstStackFrame; frame < MaximumStackFrames && frames.ReadLine() is { } line; frame++)
            { Console.Error.WriteLine(line); }
        }
        catch (Exception diagnostic)
        { throw new AggregateException(original, diagnostic); }
    }
}
