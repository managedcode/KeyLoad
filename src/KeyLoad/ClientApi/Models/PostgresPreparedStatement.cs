namespace KeyLoad.Server;

internal sealed record PostgresPreparedStatement(string Sql, int[] ParameterOids, bool Rows, int RetainedBytes, bool UsesProfileDialect = false);
