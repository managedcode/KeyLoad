namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Archive work policy validated before the standalone CLI opens files.</summary>
[ConfigurationOptions]
internal sealed class CliBackupExecutionOptions
{
    internal const string ValidationMessage = "The backup archive piece size is invalid.";
    private const int DefaultPieceBytes = 268_435_456;
    private const int MinimumPieceBytes = 1_024;
    private const int MaximumPieceBytes = 1_073_741_824;

    public int PieceBytes { get; set; } = DefaultPieceBytes;

    internal bool IsValid() => PieceBytes is >= MinimumPieceBytes and <= MaximumPieceBytes;
}
