namespace KeyLoad.AppHost.Features.OrleansHosting;

internal static class ClusterTelemetryResources
{
    private const string GenAiContent = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";
    private const string Exemplars = "OTEL_METRICS_EXEMPLAR_FILTER";
    private const string DisabledContent = "false";
    private const string DisabledExemplars = "always_off";
    private const string InvalidExporterRegistration = "The pinned Aspire project exporter registration is invalid.";

    internal static void Apply(IResourceBuilder<ProjectResource> resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var annotations = resource.Resource.Annotations;
        var exporter = annotations.OfType<OtlpExporterAnnotation>().Single();
        var exporterIndex = annotations.IndexOf(exporter);
        if (exporterIndex + 1 >= annotations.Count
            || annotations[exporterIndex + 1] is not EnvironmentCallbackAnnotation exporterEnvironment)
        {
            throw new InvalidOperationException(InvalidExporterRegistration);
        }
        annotations.Remove(exporter);
        annotations.Remove(exporterEnvironment);
        resource.WithOtlpExporterIfEndpointAvailable(OtlpProtocol.Grpc)
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables[GenAiContent] = DisabledContent;
                context.EnvironmentVariables[Exemplars] = DisabledExemplars;
            });
    }
}
