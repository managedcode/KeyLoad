using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

internal static class RequestCqrsRf3DiagnosticsScenarioPublisher
{
    internal static void EmitMalformedThenOneValidPerNode(RequestCqrsRf3DiagnosticsTestScope owner,
        RequestCqrsLifecycleEvidence lifecycle)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.Scenario);
        foreach (var resource in owner.resources)
        {
            var logger = owner.application!.Services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);
            RequestCqrsRf3DiagnosticsTestPublisher.EmitMalformed(logger);
            RequestCqrsRf3DiagnosticsTestPublisher.EmitValid(logger);
        }
    }

    internal static void EmitFortyValidLinesPerNode(RequestCqrsRf3DiagnosticsTestScope owner,
        RequestCqrsLifecycleEvidence lifecycle)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.Scenario);
        foreach (var resource in owner.resources)
        {
            var logger = owner.application!.Services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);
            RequestCqrsRf3DiagnosticsTestPublisher.EmitFortyValid(logger);
        }
    }
}
