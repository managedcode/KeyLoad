using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Creates the original bounded observers from the actual native before-start event.</summary>
internal static class ClusterRestoreRf3ObservationCreation
{
    internal static ClusterRestoreRf3OperatorObservation Operator(IServiceProvider services,
        ExecutableResource resource, CancellationToken cancellationToken)
        => new(services.GetRequiredService<ResourceLoggerService>(), resource,
            services.GetRequiredService<IOptions<TestExecutionOptions>>().Value.CleanupOutputCharacters,
            cancellationToken);

    internal static ClusterRestoreRf3StageObservation Held(IServiceProvider services,
        ExecutableResource resource, Guid operationId, NativeClusterRestoreStage stage, CancellationToken cancellationToken)
        => new(services.GetRequiredService<ResourceLoggerService>(), resource, operationId, stage,
            services.GetRequiredService<IOptions<TestExecutionOptions>>().Value.CleanupOutputCharacters,
            cancellationToken);
}
