using KeyLoad.Storage;

namespace KeyLoad.Core.TimeSeries;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.SampleReadScope)]
internal sealed record SampleReadScope(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.SampleReadScopeFields.Principal)] PrincipalRecord Principal,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.SampleReadScopeFields.Resource)] ResourceDefinition Resource,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.SampleReadScopeFields.Prefix)] byte[] Prefix)
{
    private const string InvalidSeriesIdentity = "The time-series read identity is invalid.";

    internal static SampleReadScope Open(DatabaseEngine database, IKeyValueView view, string principalId,
        PartitionRef partition, string set, string seriesId)
    {
        ArgumentNullException.ThrowIfNull(database) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(view) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        EngineResourceAccess.ValidatePartition(partition);
        if (string.IsNullOrWhiteSpace(principalId))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, InvalidSeriesIdentity);
        }

        ValidateIdentifier(set);
        ValidateIdentifier(seriesId);
        var principal = database.Principal(view, principalId, database.EvaluationClock.GetUtcNow());
        database.Authorization.Require(principal, partition, set, Capability.SeriesRead);
        var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(view, partition, set, ResourceKind.TimeSeries);
        return new(principal, resource, SampleReadKeys.Prefix(partition, set, seriesId));
    }

    private static void ValidateIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidSeriesIdentity);
        }

        JsonData.Identifier(value);
    }
}
