using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.OrleansExecution.Helpers;

internal static class RequestCqrsClusterDiagnosticsScenarioPublisher
{
    internal static void EmitMalformedThenOneValidPerNode(RequestCqrsClusterDiagnosticsTestScope owner,
        RequestCqrsLifecycleEvidence lifecycle)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.Scenario);
        foreach (var resource in owner.resources)
        {
            var logger = owner.application!.Services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);
            RequestCqrsClusterDiagnosticsTestPublisher.EmitMalformed(logger);
            RequestCqrsClusterDiagnosticsTestPublisher.EmitValid(logger);
        }
    }

    internal static void EmitFortyValidLinesPerNode(RequestCqrsClusterDiagnosticsTestScope owner,
        RequestCqrsLifecycleEvidence lifecycle)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.Scenario);
        foreach (var resource in owner.resources)
        {
            var logger = owner.application!.Services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);
            RequestCqrsClusterDiagnosticsTestPublisher.EmitFortyValid(logger);
        }
    }
}
