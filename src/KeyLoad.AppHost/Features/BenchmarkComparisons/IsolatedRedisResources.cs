using System.Globalization;
using System.Security.Cryptography;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedRedisResources
{
    private const string Target = "Redis";
    private const string Version = "8.4.0";
    private const string ImagePrefix = "docker.io/library/redis:";
    private const string Primary = "primary";
    private const string NativePrimaryHost = "primary.dev.internal";
    private const string PasswordParameter = "redis-password";
    private const string PasswordEnvironment = "KEYLOAD_REDIS_PASSWORD";
    private const string PrimaryEnvironment = "KEYLOAD_REDIS_PRIMARY";
    private const string ConnectionSetting = "ConnectionString";
    private const string ReplicaSettingPrefix = "ReplicaConnections__";
    private const string ScriptName = "IsolatedRedisBootstrap.sh";
    private const string ScriptDirectory = "Features/BenchmarkComparisons";
    private const string ScriptTarget = "/bootstrap/isolated-redis.sh";
    private const string Shell = "/bin/sh";
    private const string UserArgument = "--user";
    private const string InvalidSelection = "IsolatedRedisSelectionInvalid";
    private const string MissingBootstrap = "IsolatedRedisBootstrapMissing";
    private const int SecretBytes = 32;
    private static readonly string[] NodeNames = [Primary, "replica1", "replica2"];

    internal static void Add(IsolatedResourceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Selection.Validate();
        if (context.Selection.Target != Target)
        {
            throw new InvalidOperationException(InvalidSelection);
        }
        var script = Path.Combine(context.Builder.AppHostDirectory, ScriptDirectory, ScriptName);
        if (!File.Exists(script))
        {
            throw new InvalidOperationException(MissingBootstrap);
        }
        var password = context.Builder.AddParameter(PasswordParameter,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes)), secret: true);
        var user = ClusterContainerUser.Resolve(context.Builder);
        IResourceBuilder<RedisResource>? primary = null;
        for (var index = 0; index < context.Selection.NodeCount; index++)
        {
            var node = AddNode(context, index, password, script, user);
            if (index == 0)
            {
                primary = node;
                context.BindSetting(ConnectionSetting, node.Resource.ConnectionStringExpression);
            }
            else
            {
                node.WithEnvironment(PrimaryEnvironment, NativePrimaryHost).WaitFor(primary!);
                context.BindSetting(ReplicaSettingPrefix + (index - 1).ToString(CultureInfo.InvariantCulture),
                    node.Resource.ConnectionStringExpression);
            }
            context.BindEndpoint(index, node, node.Resource.PrimaryEndpoint.EndpointName);
        }
        context.BindImage(ImagePrefix + Version + "@" + BenchmarkResources.RedisDigest);
    }

    private static IResourceBuilder<RedisResource> AddNode(IsolatedResourceContext context, int index,
        IResourceBuilder<ParameterResource> password, string script, string? user)
    {
        var name = NodeNames[index];
        var directory = context.DataDirectory(name);
        ClusterProfileStore.PrepareDirectory(directory);
        var node = context.Builder.AddRedis(name, password: password)
            .WithImageTag(Version).WithImageSHA256(BenchmarkResources.RedisDigest[7..])
            .WithContainerNetworkAlias(name).WithDataBindMount(directory)
            .WithBindMount(script, ScriptTarget, isReadOnly: true).WithEntrypoint(Shell)
            .WithEnvironment(PasswordEnvironment, password)
            .WithArgs(static arguments =>
            {
                arguments.Args.Clear();
                arguments.Args.Add(ScriptTarget);
            });
        if (user is not null)
        {
            node.WithContainerRuntimeArgs(UserArgument, user);
        }
        return node;
    }
}
