namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3BackpressureProtocol
{
    internal const string Collection = "wirebackpressure";
    internal const string Sql = "SELECT TOP (4) * FROM wirebackpressure ORDER BY id ASC";
    internal const string Identity = "delivery-";
    internal const int Rows = 4;
    internal const int PayloadCharacters = 524288;
    internal const int ReceiveBytes = 1024;
    internal const int MaximumEffectiveReceiveBytes = 65536;
    internal const int MaximumMetadataBytes = 65536;
    internal const int MaximumJsonBytes = 16777216;
    internal const int MaximumBackendFrames = 16;
    internal const char PayloadCharacter = 'x';
    internal const string IdPrefix = "delivery-";
    internal const byte Password = (byte)'p';
    internal const byte SimpleQuery = (byte)'Q';
    internal const byte DataRow = (byte)'D';
    internal const byte Description = (byte)'T';
    internal const byte Backend = (byte)'K';
    internal const byte Ready = (byte)'Z';
    internal const int CancelCode = 80877102;
}
