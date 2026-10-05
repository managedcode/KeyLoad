using System.Globalization;
using System.Security.Cryptography;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedQdrantResources
{
    private const string Target = "Qdrant";
    private const string NodePrefix = "isolated-qdrant-";
    private const string KeyName = "isolated-qdrant-key";
    private const string KeySetting = "ApiKey";
    private const string HttpEndpoint = "http";
    private const string ImageTag = "v1.17.1";
    private const string Image = "docker.io/qdrant/qdrant:" + ImageTag;
    private const string InvalidSelection = "The selected resource helper cannot serve this target.";

    internal static void Add(IsolatedResourceContext context)
    {
        const int CountValue = 32;
        const int IndexInitialValue = 1;
        const int ElementIndex = 7;
        const int Step = 1;
        const string ReferenceText = "@";

        ArgumentNullException.ThrowIfNull(context);
        context.Selection.Validate();
        if (context.Selection.Target != Target)
        {
            throw new InvalidOperationException(InvalidSelection);
        }
        var key = context.Builder.AddParameter(KeyName, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(CountValue)), secret: true);
        IResourceBuilder<QdrantServerResource>? first = null;
        for (var index = IndexInitialValue; index <= context.Selection.NodeCount; index++)
        {
            var name = NodePrefix + index.ToString(CultureInfo.InvariantCulture);
            var node = context.Builder.AddQdrant(name, key)
                .WithImageTag(ImageTag).WithImageSHA256(BenchmarkResources.QdrantDigest[ElementIndex..])
                .WithDataBindMount(context.DataDirectory(name));
            IsolatedQdrantBootstrap.Configure(node, name, context.Selection.NodeCount, index);
            if (first is { } seed)
            {
                node.WaitFor(seed);
            }
            first ??= node;
            context.BindEndpoint(index - Step, node, HttpEndpoint);
        }
        context.BindSetting(KeySetting, key);
        context.BindImage(Image + ReferenceText + BenchmarkResources.QdrantDigest);
    }
}
