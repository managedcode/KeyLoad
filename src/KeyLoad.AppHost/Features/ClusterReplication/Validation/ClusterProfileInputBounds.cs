using KeyLoad.AppHost.Features.ClusterReplication;
using Microsoft.Extensions.Options;

internal static class ClusterProfileInputBounds
{
    private const int EmptyProfileBytes = 0;

    internal static string Root(string dataRoot, IOptions<ClusterProfileExecutionOptions> options)
    {
        var root = Path.GetFullPath(dataRoot);
        var policy = Validated(options);
        if (root.Length > policy.MaximumPathCharacters)
        { throw new InvalidOperationException(ClusterProfileStore.InvalidProfile); }
        return root;
    }

    internal static void Bytes(byte[] bytes, IOptions<ClusterProfileExecutionOptions> options)
    {
        var policy = Validated(options);
        if (bytes.Length == EmptyProfileBytes || bytes.Length > policy.MaximumProfileBytes)
        { throw new InvalidOperationException(ClusterProfileStore.InvalidProfile); }
    }

    private static ClusterProfileExecutionOptions Validated(IOptions<ClusterProfileExecutionOptions> options)
    {
        var policy = options.Value;
        if (!policy.IsValid())
        { throw new OptionsValidationException(Options.DefaultName, typeof(ClusterProfileExecutionOptions), [ClusterProfileExecutionOptions.ValidationMessage]); }
        return policy;
    }
}
