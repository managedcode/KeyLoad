using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

[ConfigurationOptions]
internal sealed class PostgresSessionOptions
{
    internal const string Section = "KeyLoad:RuntimeSql";
    private const string Invalid = "Execution SQL endpoint, transport and session limits are invalid.";
    private const int MaximumPort = 65535;
    private const int MaximumSlots = 8;
    private const int MaximumStartup = 65536;
    private const int MaximumFrame = 33554432;
    private const int MaximumRetained = 1048576;
    private const long MaximumTotalRetained = 134217728;
    private const int Minimum = 1;
    private const int MinimumFrame = 256;
    internal const int RelationalDialect = SqlOperationProtocol.DefaultQueryDialectVersion;

    public int Port
    {
        get; set;
    }
    public int QueryDialectVersion { get; set; } = SqlOperationProtocol.DefaultQueryDialectVersion;
    public bool AllowFullScan
    {
        get; set;
    }
    public bool RequireTls { get; set; } = true;
    public int MaximumStartupBytes { get; set; } = MaximumStartup;
    public int MaximumFrameBytes { get; set; } = MaximumFrame;
    public int MaximumStatements { get; set; } = MaximumSlots;
    public int MaximumPortals { get; set; } = MaximumSlots;
    public int MaximumRetainedBytes { get; set; } = MaximumRetained;
    public long MaximumTotalRetainedBytes { get; set; } = MaximumTotalRetained;
    public Dictionary<string, PartitionRef> Databases { get; set; } = new(StringComparer.Ordinal);

    internal void Validate()
    {
        if (Port is < Minimum or > MaximumPort
            || MaximumStartupBytes is < Minimum or > MaximumStartup
            || MaximumFrameBytes is < MinimumFrame or > MaximumFrame
            || MaximumStatements is < Minimum or > MaximumSlots
            || MaximumPortals is < Minimum or > MaximumSlots
            || MaximumRetainedBytes is < Minimum or > MaximumRetained
            || MaximumTotalRetainedBytes is < Minimum or > MaximumTotalRetained
            || QueryDialectVersion != SqlOperationProtocol.DefaultQueryDialectVersion
            || MaximumRetainedBytes > MaximumTotalRetainedBytes || Databases.Count is < Minimum or > MaximumSlots
            || Databases.Any(item => string.IsNullOrWhiteSpace(item.Key) || item.Value is null
                || string.IsNullOrWhiteSpace(item.Value.TenantId) || string.IsNullOrWhiteSpace(item.Value.DatabaseId)
                || string.IsNullOrWhiteSpace(item.Value.TransactionDomainId) || string.IsNullOrWhiteSpace(item.Value.PartitionKey)))
        {
            throw new OptionsValidationException(Section, typeof(PostgresSessionOptions), [Invalid]);
        }
    }
}
