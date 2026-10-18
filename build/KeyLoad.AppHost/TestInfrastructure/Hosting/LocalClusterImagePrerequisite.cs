using KeyLoad.AppHost.Features.TestInfrastructure.Execution;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class LocalClusterImagePrerequisite
{
    internal const string ResourceName = "prepare-local-cluster-server-image";
    private const string Script = "scripts/Features/TestInfrastructure/local-server-image.mjs";

    internal static IResourceBuilder<ExecutableResource> Add(
        IDistributedApplicationBuilder builder, string root, LocalClusterImageExecution execution)
    {
        const char SlashCharacter = '/';
        const string MessageText = "Local CLUSTER image producer source is missing.";
        const string CommandText = "node";
        const string ArgsText = "prepare";

        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(execution);
        var scriptPath = Path.Combine(root, Script.Replace(SlashCharacter, Path.DirectorySeparatorChar));
        if (!File.Exists(scriptPath))
        {
            throw new InvalidOperationException(MessageText);
        }

        builder.Services.AddSingleton(services => new LocalClusterImageCleanup(execution, scriptPath, AppHostOptionsRegistration.Get(builder).TestExecution, services.GetRequiredService<TimeProvider>()));
        return builder.AddExecutable(ResourceName, CommandText, root, [scriptPath, ArgsText, execution.Tag, execution.ReceiptPath]);
    }
}
