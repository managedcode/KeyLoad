using KeyLoad.Storage;

namespace KeyLoad.Core.Features.TimeSeries;

internal sealed record SampleReadScope(PrincipalRecord Principal, ResourceDefinition Resource, byte[] Prefix)
{
    private const string InvalidSeriesIdentity = "The time-series read identity is invalid.";

    internal static SampleReadScope Open(DatabaseEngine database, IKeyValueView view, string principalId,
        PartitionRef partition, string set, string seriesId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(view);
        DatabaseEngine.ValidatePartition(partition);
        if (string.IsNullOrWhiteSpace(principalId))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, InvalidSeriesIdentity);
        }

        ValidateIdentifier(set);
        ValidateIdentifier(seriesId);
        var principal = database.Principal(view, principalId, database.EvaluationClock.GetUtcNow());
        database.Authorization.Require(principal, partition, set, Capability.SeriesRead);
        var resource = database.Resource(view, partition, set, ResourceKind.TimeSeries);
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
