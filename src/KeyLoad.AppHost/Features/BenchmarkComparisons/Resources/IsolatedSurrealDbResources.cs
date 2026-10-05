namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedSurrealDbResources
{
    private const string Target = "SurrealDB";
    private const string Name = "isolated-surrealdb";
    private const string Image = "surrealdb/surrealdb";
    private const string ImageTag = "v3.2.4";
    internal const string ImageReference = "docker.io/surrealdb/surrealdb:v3.2.4@";
    private const string DataMount = "/data";
    private const string Endpoint = "http";
    private const int Port = 8000;
    private const string User = "root";
    private const string PasswordEnvironment = "SURREAL_PASS";
    private const string UserEnvironment = "SURREAL_USER";
    private const string Invalid = "IsolatedSurrealDbSelectionInvalid";

    internal static void Add(IsolatedResourceContext context)
    {
        const int SupportedNodeCount = 1;
        const string NameText = "isolated-surrealdb-password";
        const int CountValue = 32;
        const int ElementIndex = 7;
        const string ResultText = "start";
        const string AddResultText = "--bind";
        const string PathText = "/ready";
        const string AddNameText = "Password";
        const int IndexValue = 0;

        ArgumentNullException.ThrowIfNull(context);
        context.Selection.Validate();
        if (context.Selection.Target != Target || context.Selection.NodeCount != SupportedNodeCount)
        {
            throw new InvalidOperationException(Invalid);
        }

        var directory = context.DataDirectory(Name);
        ClusterProfileStore.PrepareDirectory(directory);
        var password = context.Builder.AddParameter(NameText,
            Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(CountValue)), secret: true);
        var node = context.Builder.AddContainer(Name, Image, ImageTag)
            .WithImageSHA256(BenchmarkResources.SurrealDbDigest[ElementIndex..])
            .WithContainerNetworkAlias(Name)
            .WithBindMount(directory, DataMount)
            .WithEnvironment(UserEnvironment, User)
            .WithEnvironment(PasswordEnvironment, password)
            .WithHttpEndpoint(targetPort: Port, name: Endpoint)
            .WithArgs(ResultText, AddResultText, "0.0.0.0:8000", "rocksdb:/data/benchmark.db")
            .WithHttpHealthCheck(PathText);
        if (ClusterContainerUser.Resolve(context.Builder) is { } user)
        {
            node.WithContainerRuntimeArgs("--user", user);
        }

        context.BindSetting(nameof(User), User);
        context.BindSetting(AddNameText, password);
        context.BindEndpoint(IndexValue, node, Endpoint);
        context.BindImage(ImageReference + BenchmarkResources.SurrealDbDigest);
    }
}
