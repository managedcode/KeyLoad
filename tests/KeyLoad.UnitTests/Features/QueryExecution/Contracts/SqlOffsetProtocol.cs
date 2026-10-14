using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlOffsetProtocol
{
    internal const string Root = "root";
    internal const string Reader = "offset-reader";
    internal const string Left = "offsetleft";
    internal const string Right = "offsetright";
    internal const string Order = "order_id";
    internal const string Foreign = "customer_id";
    internal const string Title = "title";
    internal const string Id = "id";
    internal const string Name = "name";
    internal const string LeftAlias = "l";
    internal const string RightAlias = "r";
    internal const string TitlePath = "/title";
    internal const string OrderPath = "/order_id";
    internal const string ForeignPath = "/customer_id";
    internal const string IdPath = "/id";
    internal const string NamePath = "/name";
    internal const int PageSize = 1;
    internal const int InitialOffset = 1;
    internal const int WindowBound = 4;
    internal const int Q1 = 1;
    internal const int Q2 = 2;
    internal const long InitialRevision = 1;
    internal const long GrantEpoch = 1;
    internal const long DenyEpoch = 2;
    internal const long RepairEpoch = 3;
    internal const int ColdCuts = 2;
    internal const string ScalarSql = "SELECT title FROM offsetleft ORDER BY order_id OFFSET 1 ROWS FETCH NEXT 1 ROW ONLY";
    internal const string JoinSql = "SELECT l.title AS title,r.name AS name FROM offsetleft AS l INNER JOIN offsetright AS r ON l.customer_id=r.id ORDER BY l.order_id OFFSET 1 ROWS FETCH NEXT 1 ROW ONLY";
    internal static AstQueryRequest Scalar(PartitionRef p) => new(p,
        new(Left, null, [new(TitlePath, Title)], null, [new(OrderPath, false)], PageSize, Offset: InitialOffset),
        AllowFullScan: true, AstVersion: Q1);
    internal static AstQueryRequest Join(PartitionRef p) => new(p,
        new(Left, LeftAlias, [new(TitlePath, Title, LeftAlias), new(NamePath, Name, RightAlias)],
            null, [new(OrderPath, false)], PageSize,
            InnerJoin: new(Right, RightAlias, ForeignPath, IdPath), Offset: InitialOffset),
        AllowFullScan: true, AstVersion: Q2);
    internal static QueryRequest ScalarRequest(PartitionRef p) => new(p, ScalarSql, AllowFullScan: true, QueryDialectVersion: Q1);
    internal static QueryRequest JoinRequest(PartitionRef p) => new(p, JoinSql, AllowFullScan: true, QueryDialectVersion: Q2);
}
