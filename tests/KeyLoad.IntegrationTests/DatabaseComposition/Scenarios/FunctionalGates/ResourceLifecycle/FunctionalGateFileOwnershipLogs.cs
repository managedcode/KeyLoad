using System.Text;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using P = KeyLoad.IntegrationTests.DatabaseComposition.FunctionalGateFileOwnershipProtocol;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

/// <summary>Retains the original bounded execution refusal prefix while separating batch traversal from stream ownership.</summary>
internal static class FunctionalGateFileOwnershipLogs
{
    internal static async Task<string> ReadAsync(FunctionalGateFixture fixture, string name, CancellationToken token)
    {
        var profile = fixture.Identity;
        string[] secrets = [profile.AdminKey, profile.SigningKey];
        var output = new StringBuilder();
        var logs = fixture.App.Services.GetRequiredService<ResourceLoggerService>();
        // DCP writes each project log under its suffixed instance name; the resource overload resolves that name.
        var resource = fixture.App.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ProjectResource>().Single(value => value.Name == name);
        try
        {
            await foreach (var batch in logs.WatchAsync(resource).WithCancellation(token).ConfigureAwait(false))
            {
                var refusal = (ReadBatch(batch, secrets, output)).Value;
                if (refusal is not null)
                {
                    return refusal;
                }
            }
        }
        catch (Exception failure)
        {
            failure.Data[P.LogEvidenceKey] = output.ToString();
            throw;
        }
        throw new InvalidDataException(P.Incomplete);
    }

    private static global::ManagedCode.Communication.Result<string?> ReadBatch(IEnumerable<LogLine> batch, string[] secrets, StringBuilder output)
    {
        foreach (var line in batch)
        {
            var text = line.Content;
            foreach (var secret in secrets.Where(secret => !string.IsNullOrEmpty(secret)))
            {
                text = text.Replace(secret, P.Redacted, StringComparison.Ordinal);
            }
            if (text.Length + P.LogLineEnding.Length > P.MaximumLogCharacters - output.Length)
            {
                var failure = new InvalidDataException(P.Incomplete);
                failure.Data[P.LogEvidenceKey] = output.ToString();
                throw failure;
            }
            output.Append(text).Append(P.LogLineEndingCharacter);
            if (FunctionalGateFileOwnershipAssertions.HasOriginalRefusal(output.ToString()))
            {
                return global::ManagedCode.Communication.Result<string?>.Succeed(((string?)(output.ToString())));
            }
        }
        return global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ReadBatch));
    }
}
