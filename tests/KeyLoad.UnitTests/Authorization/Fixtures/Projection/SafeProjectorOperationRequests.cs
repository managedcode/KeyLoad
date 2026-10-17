using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class SafeProjectorOperationRequests
{
    internal const string SourceAlias = "d";
    internal const string Equal = "=";
    internal const string ProtectedSql = "SELECT d.[odd.name] AS exposed FROM [safe-projector-operations] d WHERE d.id = 'a'";
    internal const string PublicAliasSql = "SELECT d.status AS [odd.name] FROM [safe-projector-operations] d WHERE d.id = 'a'";
    internal const string NullSql = "SELECT d.publicNull AS exposed FROM [safe-projector-operations] d WHERE d.id = 'a'";
    internal const string StarSql = "SELECT * FROM [safe-projector-operations] d WHERE d.id = 'a'";
    internal const string PredicateSql = "SELECT * FROM [safe-projector-operations] d WHERE d.[odd.name] = 'SAFE-PROJECTOR-PRIVATE-CANARY' ORDER BY d.id";
    internal const string OrderSql = "SELECT * FROM [safe-projector-operations] d WHERE d.id = 'a' ORDER BY d.[odd.name]";
    internal const string PageSql = "SELECT TOP (1) * FROM [safe-projector-operations] ORDER BY id";

    internal static AstQueryRequest Select(PartitionRef partition, string path, string alias) => new(partition,
        new(SafeProjectorOperationProtocol.Collection, SourceAlias, [new(path, alias)],
            new Comparison(new FieldOperand(SafeProjectorOperationProtocol.IdPath), Equal,
                ValueOperand.Create(SafeProjectorOperationProtocol.First)), [], SafeProjectorOperationProtocol.BothRows),
        AllowFullScan: true);
    internal static AstQueryRequest Usage(PartitionRef partition, bool order)
    {
        Predicate filter = order
            ? new Comparison(new FieldOperand(SafeProjectorOperationProtocol.IdPath), Equal, ValueOperand.Create(SafeProjectorOperationProtocol.First))
            : new Comparison(new FieldOperand(SafeProjectorOperationProtocol.LiteralPath), Equal, ValueOperand.Create(SafeProjectorOperationProtocol.Canary));
        var ordering = order ? SafeProjectorOperationProtocol.LiteralPath : SafeProjectorOperationProtocol.IdPath;
        return new(partition, new(SafeProjectorOperationProtocol.Collection, SourceAlias,
            [new(SafeProjectorOperationProtocol.Star, SafeProjectorOperationProtocol.Star)], filter,
            [new(ordering, false)], SafeProjectorOperationProtocol.BothRows), AllowFullScan: true);
    }
}
