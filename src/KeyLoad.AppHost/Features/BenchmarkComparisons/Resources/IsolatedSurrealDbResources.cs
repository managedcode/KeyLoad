namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedSurrealDbResources
{
    private const string Target = "SurrealDB";
    private const string Name = "isolated-surrealdb";
    private const string Image = "surrealdb/surrealdb";
    private const string ImageTag = "v3.2.4";
    private const string ImageReference = "docker.io/surrealdb/surrealdb:v3.2.4@";
    private const string DataMount = "/data";
    private const string Endpoint = "http";
    private const int Port = 8000;
    private const string User = "root";
    private const string PasswordEnvironment = "SURREAL_PASS";
    private const string UserEnvironment = "SURREAL_USER";
    private const string Invalid = "IsolatedSurrealDbSelectionInvalid";

    internal static void Add(IsolatedResourceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Selection.Validate();
        if (context.Selection.Target != Target || context.Selection.NodeCount != 1)
        {
            throw new InvalidOperationException(Invalid);
        }

        var directory = context.DataDirectory(Name);
        ClusterProfileStore.PrepareDirectory(directory);
        var password = context.Builder.AddParameter("isolated-surrealdb-password",
            Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), secret: true);
        var node = context.Builder.AddContainer(Name, Image, ImageTag)
            .WithImageSHA256(BenchmarkResources.SurrealDbDigest[7..])
            .WithContainerNetworkAlias(Name)
            .WithBindMount(directory, DataMount)
            .WithEnvironment(UserEnvironment, User)
            .WithEnvironment(PasswordEnvironment, password)
            .WithHttpEndpoint(targetPort: Port, name: Endpoint)
            .WithArgs("start", "--bind", "0.0.0.0:8000", "rocksdb:/data/benchmark.db")
            .WithHttpHealthCheck("/ready");
        if (ClusterContainerUser.Resolve(context.Builder) is { } user)
        {
            node.WithContainerRuntimeArgs("--user", user);
        }

        context.BindEndpoint(0, node, Endpoint);
        context.BindImage(ImageReference + BenchmarkResources.SurrealDbDigest);
    }
}
