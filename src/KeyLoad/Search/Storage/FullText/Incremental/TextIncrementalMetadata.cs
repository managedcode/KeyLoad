using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class TextIncrementalMetadata
{
    internal static void PersistIntent(string generationPath, TextIncrementalIntent intent,
        long maximumBytes, ReadExecutionBudget budget, IOptions<TextExecutionOptions> executionOptions,
        TextResourceOwnership? resources = null)
    {
        budget.Check();
        TextFileIO.VerifyDirectory(generationPath);
        var current = Path.Combine(generationPath, TextIncrementalProtocol.IntentFile);
        var pending = Path.Combine(generationPath, TextIncrementalProtocol.PendingIntentFile);
        if (File.Exists(current) || File.Exists(pending))
        {
            throw TextErrors.Ownership();
        }
        budget.ChargeBytes(OrleansSerialization.Measure(intent));
        TextFileIO.WriteEnvelope(pending, intent, maximumBytes, executionOptions, resources);
        if (resources is null)
        {
            File.Move(pending, current);
        }
        else
        {
            resources.MutatePhysical(() => File.Move(pending, current), budget);
        }
        budget.Check();
    }

    internal static TextIncrementalIntent ReadIntent(string generationPath, long maximumBytes,
        ReadExecutionBudget budget)
    {
        budget.Check();
        var path = Path.Combine(generationPath, TextIncrementalProtocol.IntentFile);
        TextFileIO.VerifyRegularFile(path);
        budget.ChargeBytes(new FileInfo(path).Length);
        var intent = TextFileIO.ReadEnvelope<TextIncrementalIntent>(path, maximumBytes);
        budget.Check();
        return intent;
    }

    internal static void Publish(string generationPath, TextIncrementalManifest manifest,
        long maximumBytes, ReadExecutionBudget budget, IOptions<TextExecutionOptions> executionOptions,
        TextResourceOwnership? resources = null)
    {
        budget.Check();
        TextFileIO.VerifyDirectory(generationPath);
        var current = Path.Combine(generationPath, TextIncrementalProtocol.ManifestFile);
        var pending = Path.Combine(generationPath, TextIncrementalProtocol.PendingManifestFile);
        if (File.Exists(pending))
        {
            throw TextErrors.Ownership();
        }
        if (File.Exists(current))
        {
            TextFileIO.VerifyRegularFile(current);
        }
        budget.ChargeBytes(OrleansSerialization.Measure(manifest));
        TextFileIO.WriteEnvelope(pending, manifest, maximumBytes, executionOptions, resources);
        if (resources is null)
        {
            File.Move(pending, current, overwrite: true);
        }
        else
        {
            resources.MutatePhysical(() => File.Move(pending, current, overwrite: true), budget);
        }
        budget.Check();
    }

    internal static TextIncrementalManifest ReadManifest(string generationPath, long maximumBytes,
        ReadExecutionBudget budget)
    {
        budget.Check();
        var path = Path.Combine(generationPath, TextIncrementalProtocol.ManifestFile);
        TextFileIO.VerifyRegularFile(path);
        budget.ChargeBytes(new FileInfo(path).Length);
        var manifest = TextFileIO.ReadEnvelope<TextIncrementalManifest>(path, maximumBytes);
        budget.Check();
        return manifest;
    }
}
