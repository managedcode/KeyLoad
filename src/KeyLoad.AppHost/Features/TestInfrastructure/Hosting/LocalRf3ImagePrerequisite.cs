using KeyLoad.AppHost.Features.TestInfrastructure.Execution;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class LocalRf3ImagePrerequisite
{
    internal const string ResourceName = "prepare-local-rf3-server-image";
    private const string Script = "scripts/Features/TestInfrastructure/local-server-image.mjs";

    internal static IResourceBuilder<ExecutableResource> Add(
        IDistributedApplicationBuilder builder, string root, LocalRf3ImageExecution execution)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(execution);
        var scriptPath = Path.Combine(root, Script.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(scriptPath))
        {
            throw new InvalidOperationException("Local RF3 image producer source is missing.");
        }

        builder.Services.AddSingleton(new LocalRf3ImageCleanup(execution, scriptPath, AppHostOptionsRegistration.Get(builder).TestExecution));
        return builder.AddExecutable(ResourceName, "node", root, [scriptPath, "prepare", execution.Tag, execution.ReceiptPath]);
    }
}
