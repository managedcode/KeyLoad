namespace KeyLoad.AppHost.Features.ClusterRouting;

internal static class ClusterTelemetryResources
{
    private const string GenAiContent = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";
    private const string Exemplars = "OTEL_METRICS_EXEMPLAR_FILTER";
    private const string DisabledContent = "false";
    private const string DisabledExemplars = "always_off";

    internal static void Apply(IResourceBuilder<ProjectResource> resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        resource.WithOtlpExporterIfEndpointAvailable(OtlpProtocol.Grpc)
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables[GenAiContent] = DisabledContent;
                context.EnvironmentVariables[Exemplars] = DisabledExemplars;
            });
    }
}
