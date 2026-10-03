namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedMongoBootstrap
{
    private const string SourceDirectory = "Features/BenchmarkComparisons";
    private const string EntryFile = "IsolatedMongoEntry.sh";
    private const string ClientFile = "IsolatedMongoInitiate.js";
    private const string EntryTarget = "/bootstrap/isolated-mongo.sh";
    private const string ClientTarget = "/bootstrap/isolated-mongo.js";
    private const string ClientName = "isolated-mongo-bootstrap";
    private const string PasswordEnvironment = "MONGO_INITDB_ROOT_PASSWORD";
    private const string UserEnvironment = "MONGO_INITDB_ROOT_USERNAME";
    private const string KeyEnvironment = "KEYLOAD_MONGO_REPLICATION_KEY";
    private const string MembersEnvironment = "KEYLOAD_MONGO_MEMBERS";
    private const string SetEnvironment = "KEYLOAD_MONGO_REPLICA_SET";
    private const string KeyFile = "/tmp/keyload-mongo-key/replication.key";
    private const string Data = "/data/db";
    private const string Shell = "/bin/sh";
    private const string Mongod = "mongod";
    private const string BindAll = "--bind_ip_all";
    private const string Authenticate = "--auth";
    private const string SetOption = "--replSet";
    private const string KeyOption = "--keyFile";
    private const string ReplicaSet = "benchmark";
    private const string MongoShell = "mongosh";
    private const string Quiet = "--quiet";
    private const string NoDatabase = "--nodb";
    private const string MissingBootstrap = "IsolatedMongoBootstrapMissing";
    private const int DigestPrefixLength = 7;

    internal static (string Entry, string Client) FindScripts(IsolatedResourceContext context)
    {
        var source = Path.Combine(context.Builder.AppHostDirectory, SourceDirectory);
        var entry = Path.Combine(source, EntryFile);
        var client = Path.Combine(source, ClientFile);
        if (!File.Exists(entry) || !File.Exists(client))
        {
            throw new InvalidOperationException(MissingBootstrap);
        }
        return (entry, client);
    }

    internal static void ConfigureNode(IResourceBuilder<ContainerResource> node, string directory, string script,
        string user, IResourceBuilder<ParameterResource> password, IResourceBuilder<ParameterResource>? key)
    {
        node.WithBindMount(directory, Data).WithBindMount(script, EntryTarget, isReadOnly: true)
            .WithEnvironment(UserEnvironment, user).WithEnvironment(PasswordEnvironment, password)
            .WithEntrypoint(Shell).WithArgs(EntryTarget, Mongod, BindAll, Authenticate);
        if (key is not null)
        {
            node.WithEnvironment(KeyEnvironment, key).WithArgs(SetOption, ReplicaSet, KeyOption, KeyFile);
        }
    }

    internal static void AddClient(IsolatedResourceContext context, IEnumerable<IResourceBuilder<ContainerResource>> nodes,
        string script, string user, IResourceBuilder<ParameterResource> password, string replicaSet, string image, string tag, string hosts)
    {
        var client = context.Builder.AddContainer(ClientName, image, tag).WithImageSHA256(BenchmarkResources.MongoDigest[DigestPrefixLength..])
            .WithBindMount(script, ClientTarget, isReadOnly: true).WithArgs(MongoShell, Quiet, NoDatabase, ClientTarget)
            .WithEnvironment(UserEnvironment, user).WithEnvironment(PasswordEnvironment, password)
            .WithEnvironment(MembersEnvironment, hosts).WithEnvironment(SetEnvironment, replicaSet);
        foreach (var node in nodes)
        {
            client.WaitFor(node);
        }
        context.Runner.WaitForCompletion(client);
    }
}
