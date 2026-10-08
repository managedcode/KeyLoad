using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalMetadata
{
    internal static void PersistIntent(string generationPath, NativeTextIncrementalIntent intent,
        long maximumBytes, ReadExecutionBudget budget, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        budget.Check();
        NativeTextFileIO.VerifyDirectory(generationPath);
        var current = Path.Combine(generationPath, NativeTextIncrementalProtocol.IntentFile);
        var pending = Path.Combine(generationPath, NativeTextIncrementalProtocol.PendingIntentFile);
        if (File.Exists(current) || File.Exists(pending))
        { throw NativeTextErrors.Ownership(); }
        budget.ChargeBytes(NativeSerialization.Measure(intent));
        NativeTextFileIO.WriteEnvelope(pending, intent, maximumBytes, executionOptions);
        File.Move(pending, current);
        budget.Check();
    }

    internal static NativeTextIncrementalIntent ReadIntent(string generationPath, long maximumBytes,
        ReadExecutionBudget budget)
    {
        budget.Check();
        var path = Path.Combine(generationPath, NativeTextIncrementalProtocol.IntentFile);
        NativeTextFileIO.VerifyRegularFile(path);
        budget.ChargeBytes(new FileInfo(path).Length);
        var intent = NativeTextFileIO.ReadEnvelope<NativeTextIncrementalIntent>(path, maximumBytes);
        budget.Check();
        return intent;
    }

    internal static void Publish(string generationPath, NativeTextIncrementalManifest manifest,
        long maximumBytes, ReadExecutionBudget budget, IOptions<NativeTextExecutionOptions> executionOptions)
    {
        budget.Check();
        NativeTextFileIO.VerifyDirectory(generationPath);
        var current = Path.Combine(generationPath, NativeTextIncrementalProtocol.ManifestFile);
        var pending = Path.Combine(generationPath, NativeTextIncrementalProtocol.PendingManifestFile);
        if (File.Exists(pending))
        { throw NativeTextErrors.Ownership(); }
        if (File.Exists(current))
        { NativeTextFileIO.VerifyRegularFile(current); }
        budget.ChargeBytes(NativeSerialization.Measure(manifest));
        NativeTextFileIO.WriteEnvelope(pending, manifest, maximumBytes, executionOptions);
        File.Move(pending, current, overwrite: true);
        budget.Check();
    }

    internal static NativeTextIncrementalManifest ReadManifest(string generationPath, long maximumBytes,
        ReadExecutionBudget budget)
    {
        budget.Check();
        var path = Path.Combine(generationPath, NativeTextIncrementalProtocol.ManifestFile);
        NativeTextFileIO.VerifyRegularFile(path);
        budget.ChargeBytes(new FileInfo(path).Length);
        var manifest = NativeTextFileIO.ReadEnvelope<NativeTextIncrementalManifest>(path, maximumBytes);
        budget.Check();
        return manifest;
    }
}
