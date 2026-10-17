namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertOwnershipEpochLiteral
{
    internal const long ZeroEpoch = 0;
    internal const long NegativeEpoch = -1;
    internal const long StaleEpoch = 1;
    internal const long PublishedEpoch = 2;
    internal const long FutureEpoch = 3;
    internal const int NoParameters = 0;
    internal const string InsertSql = "INSERT INTO [retained-sql-insert] (id,title) VALUES (N'target-row',N'after-repair')";
    internal const string Stale = "The partition ownership epoch is stale.";
    internal const string MissingOperation = "The admitted native SQL mutation was not compiled.";
    internal const string GuidFormat = "N";
    internal const string EpochFormat = "X16";
    internal static readonly Guid StaleId = Guid.Parse("5dbf18be-9590-43b6-9fd4-0c75ab9e4ce3");
    internal static readonly Guid FutureId = Guid.Parse("fe9844e8-d567-4bf7-9525-b5645924a479");
    internal static string Arm(Guid id, long epoch) => "SET CONTEXT_INFO 0x" + id.ToString(GuidFormat)
        + epoch.ToString(EpochFormat, global::System.Globalization.CultureInfo.InvariantCulture);
}

