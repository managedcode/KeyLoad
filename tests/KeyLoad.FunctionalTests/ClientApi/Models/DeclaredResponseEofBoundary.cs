namespace KeyLoad.FunctionalTests.ClientApi;

/// <summary>Passive coordination of one real execution handler and its physical response abort.</summary>
internal sealed class DeclaredResponseEofBoundary
{
    internal TaskCompletionSource<Task> Retained
    {
        get;
    } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Flushed
    {
        get;
    } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release
    {
        get;
    } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Aborted
    {
        get;
    } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
