namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Owns the same original task and, once observed, its exact original expected failure object.</summary>
internal sealed class NativeDropExpectedOriginal(Task original)
{
    internal Task Original { get; } = original;
    internal Exception? ExpectedFailure
    {
        get; set;
    }
}
