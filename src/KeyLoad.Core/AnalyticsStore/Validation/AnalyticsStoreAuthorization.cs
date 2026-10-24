using KeyLoad.Storage;

namespace KeyLoad.Core.AnalyticsStore;

internal static class AnalyticsStoreAuthorization
{
    private const string PathSeparator = "/";
    private const string EscapeCharacter = "~";
    private const string EscapedTilde = "~0";
    private const string EscapedSeparator = "~1";
    internal const string LinksPath = "/links";
    internal static void Field(DatabaseEngine database, PrincipalRecord principal, ResourceDefinition resource,
        string path, bool write)
    {
        if (database.Authorization.GetEffectiveFieldPolicies(resource, path)
            .Any(policy => !database.Authorization.CanReadFieldSchema(principal, policy)))
        {
            throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.UnavailableBinding);
        }

        if (write)
        {
            database.Authorization.RequireFieldWrite(principal, resource, path);
        }
        else
        {
            database.Authorization.RequireFieldUse(principal, resource, path);
        }
    }

    internal static void WriteFields(DatabaseEngine database, PrincipalRecord principal, ResourceDefinition resource)
    {
        var schema = resource.AnalyticsSchema!;
        foreach (var name in schema.Dimensions)
        {
            Field(database, principal, resource, Path(AnalyticsStoreValidation.Dimensions, name), true);
        }

        foreach (var name in schema.Metrics)
        {
            Field(database, principal, resource, Path(AnalyticsStoreValidation.Metrics, name), true);
        }

        Field(database, principal, resource, LinksPath, true);
    }

    internal static string Path(string parent, string name)
        => PathSeparator + parent + PathSeparator + name.Replace(EscapeCharacter, EscapedTilde, StringComparison.Ordinal)
            .Replace(PathSeparator, EscapedSeparator, StringComparison.Ordinal);

    internal static AnalyticsStoredEvent? Previous(IKeyValueView view, PartitionRef partition, string store, string id)
        => view.GetRecord<AnalyticsStoredEvent>(AnalyticsStoreKeys.Event(partition, store, id));
}
