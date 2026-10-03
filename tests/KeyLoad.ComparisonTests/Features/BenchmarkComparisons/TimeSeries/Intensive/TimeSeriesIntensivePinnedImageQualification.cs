using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed record TimeSeriesIntensivePinnedImageObservation(TimeSeriesIntensivePinnedImageConfig Image,
    IReadOnlyDictionary<string, string> Facts, string ContainerId);

internal static class TimeSeriesIntensivePinnedImageQualification
{
    internal static async Task<TimeSeriesIntensivePinnedImageFacts> RunAsync(CancellationToken token)
    {
        var context = TimeSeriesIntensivePinnedImageContext.Read();
        var evidence = await TimeSeriesIntensivePinnedImageEvidence.CreateAsync(token);
        var nonce = Guid.NewGuid().ToString(TimeSeriesIntensivePinnedImageFields.NonceFormat);
        var name = TimeSeriesIntensivePinnedImageProtocol.ContainerPrefix + nonce;
        Exception? primary = null;
        TimeSeriesIntensivePinnedImageObservation? observed = null;
        TimeSeriesIntensivePinnedImageCleanupResult cleanup;
        try
        {
            observed = await ObserveAsync(evidence, name, nonce, token);
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            cleanup = await TimeSeriesIntensivePinnedImageCleanup.RunAsync(evidence, name, nonce, primary);
            var failure = await RetainCleanupAsync(evidence, cleanup, context, primary);
            if (primary is null && failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        if (observed is null || !cleanup.Completed)
        {
            throw new InvalidOperationException(TimeSeriesIntensivePinnedImageProtocol.CleanupFailure);
        }

        var facts = new TimeSeriesIntensivePinnedImageFacts(TimeSeriesIntensivePinnedImageProtocol.SchemaVersion,
            context, observed.Image, observed.Facts, name, observed.ContainerId, cleanup.Completed, evidence.Commands);
        await evidence.WriteJsonAsync(TimeSeriesIntensivePinnedImageProtocol.FactsFile, facts);
        return facts;
    }

    private static async Task<TimeSeriesIntensivePinnedImageObservation> ObserveAsync(
        TimeSeriesIntensivePinnedImageEvidence evidence, string name, string nonce, CancellationToken token)
    {
        var version = await RequiredAsync(evidence, TimeSeriesIntensivePinnedImageFields.DockerVersion,
            [TimeSeriesIntensivePinnedImageFields.VersionCommand, TimeSeriesIntensivePinnedImageFields.Format,
                TimeSeriesIntensivePinnedImageFields.JsonFormat], token);
        using var versionJson = JsonDocument.Parse(version.Output);
        await RequiredAsync(evidence, TimeSeriesIntensivePinnedImageFields.Pull,
            [TimeSeriesIntensivePinnedImageFields.Pull, TimeSeriesIntensivePinnedImageFields.QuietFlag,
                TimeSeriesIntensivePinnedImageProtocol.Image], token,
            TimeSeriesIntensivePinnedImageProtocol.PullSeconds);
        var inspection = await RequiredAsync(evidence, TimeSeriesIntensivePinnedImageFields.ImageInspect,
            [TimeSeriesIntensivePinnedImageFields.ImageCommand, TimeSeriesIntensivePinnedImageFields.InspectCommand,
                TimeSeriesIntensivePinnedImageFields.Format, TimeSeriesIntensivePinnedImageFields.JsonFormat,
                TimeSeriesIntensivePinnedImageProtocol.Image], token);
        var image = TimeSeriesIntensivePinnedImageInspection.ReadImage(inspection.Output);
        var create = await RequiredAsync(evidence, TimeSeriesIntensivePinnedImageFields.ProbeCreate,
            CreateArguments(name, nonce, image), token);
        var id = Encoding.UTF8.GetString(create.Output).Trim();
        var probe = await RequiredAsync(evidence, TimeSeriesIntensivePinnedImageFields.ProbeStart,
            [TimeSeriesIntensivePinnedImageFields.StartCommand, TimeSeriesIntensivePinnedImageFields.AttachFlag, name], token);
        var state = await RequiredAsync(evidence, TimeSeriesIntensivePinnedImageFields.ProbeInspect,
            [TimeSeriesIntensivePinnedImageFields.ContainerCommand, TimeSeriesIntensivePinnedImageFields.InspectCommand,
                TimeSeriesIntensivePinnedImageFields.Format, TimeSeriesIntensivePinnedImageFields.JsonFormat, name], token);
        var actualId = TimeSeriesIntensivePinnedImageInspection.ReadContainer(state.Output, name, nonce, image, requireExited: true);
        TimeSeriesIntensivePinnedImageInspection.Require(id == actualId);
        return new(image, TimeSeriesIntensivePinnedImageInspection.ReadObservations(probe.Output), actualId);
    }

    private static IReadOnlyList<string> CreateArguments(string name, string nonce, TimeSeriesIntensivePinnedImageConfig image) =>
        [TimeSeriesIntensivePinnedImageFields.CreateCommand, TimeSeriesIntensivePinnedImageFields.NameFlag, name,
            TimeSeriesIntensivePinnedImageFields.LabelFlag, TimeSeriesIntensivePinnedImageProtocol.OwnershipLabel + '=' + nonce,
            TimeSeriesIntensivePinnedImageFields.UserFlag, TimeSeriesIntensivePinnedImageProtocol.RootUser,
            TimeSeriesIntensivePinnedImageFields.NetworkFlag, TimeSeriesIntensivePinnedImageFields.None,
            TimeSeriesIntensivePinnedImageFields.EntrypointFlag, TimeSeriesIntensivePinnedImageProtocol.Shell,
            TimeSeriesIntensivePinnedImageFields.EnvironmentFlag, TimeSeriesIntensivePinnedImageFields.PgDataEnvironment
                + TimeSeriesIntensivePinnedImageProtocol.ProposedPgData,
            TimeSeriesIntensivePinnedImageFields.PullFlag, TimeSeriesIntensivePinnedImageFields.Never,
            TimeSeriesIntensivePinnedImageProtocol.Image, TimeSeriesIntensivePinnedImageFields.ShellCommandFlag,
            TimeSeriesIntensivePinnedImageProbe.Script, name, image.Entrypoint[0]];

    private static async Task<TimeSeriesIntensivePinnedImageCommand> RequiredAsync(
        TimeSeriesIntensivePinnedImageEvidence evidence, string label, IReadOnlyList<string> arguments,
        CancellationToken token, int seconds = TimeSeriesIntensivePinnedImageProtocol.OperationSeconds)
    {
        var command = await TimeSeriesIntensivePinnedImageProcess.RunAsync(TimeSeriesIntensivePinnedImageProtocol.Docker,
            arguments, seconds, token, evidence, label);
        if (command.ExitCode != 0)
        {
            throw new InvalidOperationException(TimeSeriesIntensivePinnedImageProtocol.NativeFailure);
        }

        return command;
    }

    private static async Task<Exception?> RetainCleanupAsync(TimeSeriesIntensivePinnedImageEvidence evidence,
        TimeSeriesIntensivePinnedImageCleanupResult cleanup, TimeSeriesIntensivePinnedImageContext context, Exception? primary)
    {
        primary ??= cleanup.Completed ? null : new InvalidOperationException(TimeSeriesIntensivePinnedImageProtocol.CleanupFailure);
        try
        {
            await evidence.WriteJsonAsync(TimeSeriesIntensivePinnedImageProtocol.CleanupFile, cleanup);
            if (primary is not null)
            {
                await evidence.WriteJsonAsync(TimeSeriesIntensivePinnedImageProtocol.FailureFile,
                    new { context, primaryType = primary.GetType().FullName, cleanup });
            }
        }
        catch (Exception persistence) when (primary is not null || TimeSeriesIntensivePinnedImageFailure.IsNative(persistence))
        {
            primary ??= persistence;
        }

        return primary;
    }
}
