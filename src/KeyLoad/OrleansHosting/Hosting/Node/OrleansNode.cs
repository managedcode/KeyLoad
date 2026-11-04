namespace KeyLoad.Server;

internal sealed partial class OrleansNode
{
    internal Task WaitForStartupAsync(CancellationToken cancellationToken)
        => startupWait.WaitAsync(cancellationToken);
}
