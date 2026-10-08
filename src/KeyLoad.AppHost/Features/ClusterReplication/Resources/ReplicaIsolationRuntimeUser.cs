namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Preserves the existing native AppHost service-user arguments before adding fault permissions.</summary>
internal static class ReplicaIsolationRuntimeUser
{
    private const string UserArgument = "--user";
    private const int ArgumentCount = 2;
    private const int OptionIndex = 0;
    private const int UserIndex = 1;
    private const char Separator = ':';
    private const uint RootUser = 0;
    private const string OriginalUserArgumentsRequired = "The fault cohort must retain exactly the existing native service-user arguments.";
    private const string NonRootUserRequired = "The namespace fault cohort requires the unchanged non-root service uid:gid.";

    internal static async Task<string> ReadAsync(ContainerResource resource, CancellationToken cancellationToken)
    {
        var arguments = new List<object>();
        var context = new ContainerRuntimeArgsCallbackContext(arguments, cancellationToken);
        foreach (var annotation in resource.Annotations.OfType<ContainerRuntimeArgsCallbackAnnotation>())
        { await annotation.Callback(context).ConfigureAwait(false); }
        if (arguments.Count != ArgumentCount || arguments[OptionIndex] is not string option
            || !string.Equals(option, UserArgument, StringComparison.Ordinal) || arguments[UserIndex] is not string user)
        { throw new InvalidOperationException(OriginalUserArgumentsRequired); }
        var parts = user.Split(Separator);
        if (parts.Length != ArgumentCount || !uint.TryParse(parts[OptionIndex], out var uid) || uid == RootUser
            || !uint.TryParse(parts[UserIndex], out _))
        { throw new InvalidOperationException(NonRootUserRequired); }
        return user;
    }
}
