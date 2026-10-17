namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateCompositionProtocol
{
    internal const int Resources = 3;
    internal const int Initial = 1;
    internal const int LinkedTable = 2;
    internal const int UnlinkedDocument = 2;
    internal const int UnlinkedTable = 3;
    internal const int DeletedDocument = 3;
    internal const int DeletedTable = 4;
    internal const int Absent = 0;
    internal const int PlacementVersion = 1;
    internal const string Table = "gate_sql_0";
    internal const string Kind = "composition";
    internal const string Point = "point";
    internal const string Put = "putDocument";
    internal const string Patch = "patchDocument";
    internal const string Delete = "deleteDocument";
    internal const string AnchorJson = "{\"id\":\"anchor\",\"docId\":\"anchor\",\"kind\":\"anchor\",\"name\":\"functional-gate\"}";
    internal const string InsertSql = "INSERT INTO gate_sql_0 (id,title,amount,doc_id,anchor_ref) "
        + "VALUES (@id,@title,@amount,@doc_id,@anchor_ref)";
}
