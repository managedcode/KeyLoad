using Aspire.Hosting;
using Aspire.Hosting.Azure;
using KeyLoad.AppHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeSixMembershipInfrastructure : IAsyncDisposable
{
    private const string MissingConnection = "The owned native membership connection is missing.";
    private readonly DistributedApplication application;
    private readonly AzureTableStorageResource tables;
    private Task? disposal;

    internal NativeSixMembershipInfrastructure()
    {
        (application, tables) = NativeSixMembershipResources.Create();
    }

    internal async Task<string> StartAsync(CancellationToken token)
    {
        await application.StartAsync(token).ConfigureAwait(false);
        await application.ResourceNotifications.WaitForResourceHealthyAsync(tables.Name, token).ConfigureAwait(false);
        return await tables.ConnectionStringExpression.GetValueAsync(token).ConfigureAwait(false)
            ?? throw new InvalidOperationException(MissingConnection);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return new(disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => application.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => application.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
