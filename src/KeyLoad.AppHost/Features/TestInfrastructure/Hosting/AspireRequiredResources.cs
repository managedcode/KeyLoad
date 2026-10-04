using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class AspireRequiredResources
{
    private const string MissingRunner = "The Aspire application model has no selected runner.";
    private const string ConflictingCompletion = "An Aspire dependency has incompatible expected completion exits.";

    internal static Dictionary<string, int?> ForRunner(DistributedApplication app, string runnerName)
    {
        var resources = app.Services.GetRequiredService<DistributedApplicationModel>().Resources;
        var runner = resources.SingleOrDefault(resource => string.Equals(resource.Name, runnerName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(MissingRunner);
        var dependencies = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<IResource> { runner };
        var pending = new Queue<IResource>();
        pending.Enqueue(runner);
        while (pending.TryDequeue(out var resource))
        {
            AddDependencies(resource, runner, dependencies, visited, pending);
        }
        return dependencies;
    }

    private static void AddDependencies(IResource resource, IResource runner, Dictionary<string, int?> dependencies,
        HashSet<IResource> visited, Queue<IResource> pending)
    {
        foreach (var wait in resource.Annotations.OfType<WaitAnnotation>())
        {
            if (wait.Resource is IResourceWithoutLifetime || ReferenceEquals(wait.Resource, runner))
            {
                continue;
            }
            AddContract(dependencies, wait);
            if (visited.Add(wait.Resource))
            {
                pending.Enqueue(wait.Resource);
            }
        }
    }

    private static void AddContract(Dictionary<string, int?> dependencies, WaitAnnotation wait)
    {
        int? expected = wait.WaitType == WaitType.WaitForCompletion ? wait.ExitCode : null;
        if (dependencies.TryGetValue(wait.Resource.Name, out var prior))
        {
            if (prior is not null && expected is not null && prior != expected)
            {
                throw new InvalidOperationException(ConflictingCompletion);
            }
            expected = prior is null || expected is null ? null : expected;
        }
        dependencies[wait.Resource.Name] = expected;
    }
}
