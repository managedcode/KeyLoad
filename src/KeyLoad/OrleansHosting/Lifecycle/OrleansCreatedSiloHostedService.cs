namespace KeyLoad.Server.Features.OrleansHosting;

internal sealed class OrleansCreatedSiloHostedService(IHostedService original) : IHostedService
{
    private const int NotAttempted = 0;
    private const int Attempted = 1;
    private int startAttempted;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Volatile.Write(ref startAttempted, Attempted);
        return original.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Volatile.Read(ref startAttempted) == NotAttempted
            ? Task.CompletedTask
            : original.StopAsync(cancellationToken);
}
