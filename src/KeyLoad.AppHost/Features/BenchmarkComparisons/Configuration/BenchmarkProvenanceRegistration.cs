using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

/// <summary>Preserves original environment provenance without accepting caller substitutions.</summary>
[ConfigurationBinding]
internal static class BenchmarkProvenanceRegistration
{
    private const string Source = "GITHUB_SHA";
    private const string Run = "GITHUB_RUN_ID";
    private const string Attempt = "GITHUB_RUN_ATTEMPT";
    private const string Job = "KEYLOAD_COMPARISON_JOB_ID";
    private const string Repository = "GITHUB_REPOSITORY";
    private const string Reference = "GITHUB_REF";
    private const string Workflow = "GITHUB_WORKFLOW";
    private const string Actions = "GITHUB_ACTIONS";
    private const string ImageReceipt = "KEYLOAD_IMAGE_RECEIPT";
    private const string DockerHost = "DOCKER_HOST";
    private const string DockerContext = "DOCKER_CONTEXT";

    internal static IOptions<BenchmarkProvenanceOptions> Bind()
    {
        var factory = new OptionsFactory<BenchmarkProvenanceOptions>([new ConfigureOptions<BenchmarkProvenanceOptions>(options =>
        {
            var values = Read();
            options.SourceRevision = values.SourceRevision;
            options.WorkflowRunId = values.WorkflowRunId;
            options.RunAttempt = values.RunAttempt;
            options.JobId = values.JobId;
            options.Repository = values.Repository;
            options.Reference = values.Reference;
            options.Workflow = values.Workflow;
            options.Actions = values.Actions;
            options.ImageReceipt = values.ImageReceipt;
            options.DockerHost = values.DockerHost;
            options.DockerContext = values.DockerContext;
        })], [], []);
        var options = new OptionsManager<BenchmarkProvenanceOptions>(factory);
        _ = options.Value;
        return options;
    }

    internal static void Apply(IResourceBuilder<ContainerResource> resource, IOptions<BenchmarkProvenanceOptions> options)
    {
        var value = options.Value;
        Copy(resource, Source, value.SourceRevision);
        Copy(resource, Run, value.WorkflowRunId);
        Copy(resource, Attempt, value.RunAttempt);
        Copy(resource, Job, value.JobId);
        Copy(resource, Repository, value.Repository);
        Copy(resource, Reference, value.Reference);
        Copy(resource, Workflow, value.Workflow);
    }

    private static void Copy(IResourceBuilder<ContainerResource> resource, string name, string? value)
    {
        if (value is not null) { resource.WithEnvironment(name, value); }
    }

    private static BenchmarkProvenanceOptions Read() => new()
    {
        SourceRevision = Environment.GetEnvironmentVariable(Source),
        WorkflowRunId = Environment.GetEnvironmentVariable(Run),
        RunAttempt = Environment.GetEnvironmentVariable(Attempt),
        JobId = Environment.GetEnvironmentVariable(Job),
        Repository = Environment.GetEnvironmentVariable(Repository),
        Reference = Environment.GetEnvironmentVariable(Reference),
        Workflow = Environment.GetEnvironmentVariable(Workflow),
        Actions = Environment.GetEnvironmentVariable(Actions),
        ImageReceipt = Environment.GetEnvironmentVariable(ImageReceipt),
        DockerHost = Environment.GetEnvironmentVariable(DockerHost),
        DockerContext = Environment.GetEnvironmentVariable(DockerContext)
    };
}
