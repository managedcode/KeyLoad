using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class AdminReadAuthority
{
    private const string AdministratorRequired = "Cluster administration is required.";
    private const string InvalidPageLimit = "The administration page limit must be between one and one hundred.";

    internal static void Require(DatabaseEngine database, IKeyValueView view, string principalId)
    {
        var principal = database.Principal(view, principalId, database.EvaluationClock.GetUtcNow());
        if (!principal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, AdministratorRequired); }
    }

    internal static void ValidatePage(int limit, string? cursor)
    {
        if (limit is < 1 or > AdminDashboardProtocol.MaximumPageSize)
        { throw Errors.Fail(ErrorCode.Validation, InvalidPageLimit); }
        if (cursor is not null)
        { JsonData.Identifier(cursor); }
    }

    internal static long Position(IKeyValueView view) => view.ReadOwnedValue(KeySpace.Applied.ToArray()) is { } value
        ? NativeSerialization.Deserialize<long>(value) : 0;
}
