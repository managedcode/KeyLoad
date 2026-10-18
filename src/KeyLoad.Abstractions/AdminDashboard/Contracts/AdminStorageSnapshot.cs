using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Bounded physical filesystem observation, independent of the database read cut.</summary>
/// <param name="CanonicalBytes">Observed canonical store file lengths, null when unavailable.</param>
/// <param name="BackupBytes">Observed backup file lengths, null when unavailable.</param>
/// <param name="TotalBytes">Observed node file lengths, null when unavailable.</param>
/// <param name="ObservedFiles">Number of files whose lengths were included.</param>
/// <param name="Complete">Whether all eligible files were observed within the bounds.</param>
/// <param name="Files">Finite display subset; its sum need not equal total observed bytes.</param>
/// <param name="Notice">Safe incomplete/unavailable reason without sensitive filesystem details.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminStorageSnapshot)]
public sealed record AdminStorageSnapshot([property: Orleans.Id(0)] long? CanonicalBytes, [property: Orleans.Id(2)] long? BackupBytes,
    [property: Orleans.Id(3)] long? TotalBytes, [property: Orleans.Id(4)] int ObservedFiles, [property: Orleans.Id(5)] bool Complete, [property: Orleans.Id(6)] ImmutableArray<AdminFileInfo> Files, [property: Orleans.Id(7)] string? Notice);
