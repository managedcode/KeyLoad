using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.RecoveryHost;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class SeededWalCorruptionEvidence
{
    private const int SchemaVersion = 1;
    private const string FaultClass = "complete-native-atomic-redo-payload-checksum";
    private const string Solution = "KeyLoad.slnx";
    private const string Artifacts = "artifacts";
    private const string Qualification = "qualification";
    internal static async Task<string> SaveAsync(SeededWalCorruptionTrial trial, SeededWalCorruptionProcessOwner owner,
        List<Exception> failures, CancellationToken token)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository.Parent is not null && !File.Exists(Path.Combine(repository.FullName, Solution)))
        {
            repository = repository.Parent;
        }
        if (!File.Exists(Path.Combine(repository.FullName, Solution)))
        {
            throw new InvalidOperationException("The corruption evidence repository boundary is missing.");
        }
        var directory = Path.Combine(repository.FullName, Artifacts, Qualification);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "native-wal-seeded-corruption-" + trial.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N") + ".json");
        var assemblies = new List<object>();
        foreach (var assembly in new[] { typeof(SeededWalCorruptionEvidence).Assembly,
            typeof(RecoveryHostMarker).Assembly, typeof(DatabaseEngine).Assembly, typeof(ZoneTreeStore).Assembly })
        {
            await using var image = File.OpenRead(assembly.Location);
            assemblies.Add(new
            {
                Name = assembly.GetName().Name,
                Sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(image, token))
            });
        }
        var evidence = new
        {
            SchemaVersion,
            FaultClass,
            trial.Seed,
            OriginalCaptured = trial.Original.Length > 0,
            trial.Position,
            trial.Offset,
            trial.Mask,
            Length = trial.Original.Length,
            trial.OriginalHash,
            trial.DamagedHash,
            trial.OriginalFiles,
            trial.DamagedFiles,
            trial.RefusalFiles,
            trial.RestoredFiles,
            trial.FollowUpFiles,
            Phases = owner.History,
            ObservedBeforeTrialCleanupFailureTypes = failures.Select(failure => failure.GetType().FullName).ToArray(),
            Assemblies = assemblies,
            OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            PowerLossQualified = false,
            LinuxQualified = false
        };
        await using var output = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous
        });
        await JsonSerializer.SerializeAsync(output, evidence, JsonDefaults.Options, token);
        await output.FlushAsync(token);
        return path;
    }
}
