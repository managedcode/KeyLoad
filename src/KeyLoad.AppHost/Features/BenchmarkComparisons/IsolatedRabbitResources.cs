using System.Globalization;
using System.Security.Cryptography;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedRabbitResources
{
    private const string Target = "RabbitMQ";
    private const string NodePrefix = "isolated-rabbit-";
    private const string UserName = "isolated-rabbit-user";
    private const string PasswordName = "isolated-rabbit-password";
    private const string CookieName = "isolated-rabbit-cookie";
    private const string UserSetting = "User";
    private const string PasswordSetting = "Password";
    private const string ConnectionSetting = "ConnectionString";
    private const string ManagementEndpoint = "management";
    private const string ImageTag = "4.2.4-management";
    private const string Image = "docker.io/library/rabbitmq:" + ImageTag;
    private const string InvalidSelection = "The selected resource helper cannot serve this target.";

    internal static void Add(IsolatedResourceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Selection.Validate();
        if (context.Selection.Target != Target)
        {
            throw new InvalidOperationException(InvalidSelection);
        }
        var user = Secret(context, UserName);
        var password = Secret(context, PasswordName);
        var cookie = Secret(context, CookieName);
        var configuration = IsolatedRabbitBootstrap.Write(context);
        IResourceBuilder<RabbitMQServerResource>? first = null;
        for (var index = 1; index <= context.Selection.NodeCount; index++)
        {
            var name = NodePrefix + index.ToString(CultureInfo.InvariantCulture);
            var node = context.Builder.AddRabbitMQ(name, user, password).WithManagementPlugin()
                .WithImageTag(ImageTag).WithImageSHA256(BenchmarkResources.RabbitDigest[7..])
                .WithDataBindMount(context.DataDirectory(name));
            IsolatedRabbitBootstrap.Configure(node, name, cookie, configuration);
            if (first is { } seed)
            {
                node.WaitFor(seed);
            }
            else
            {
                context.BindSetting(ConnectionSetting, node.Resource.ConnectionStringExpression);
            }
            first ??= node;
            context.BindEndpoint(index - 1, node, ManagementEndpoint);
        }
        context.BindSetting(UserSetting, user);
        context.BindSetting(PasswordSetting, password);
        context.BindImage(Image + "@" + BenchmarkResources.RabbitDigest);
    }

    private static IResourceBuilder<ParameterResource> Secret(IsolatedResourceContext context, string name)
        => context.Builder.AddParameter(name, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), secret: true);
}
