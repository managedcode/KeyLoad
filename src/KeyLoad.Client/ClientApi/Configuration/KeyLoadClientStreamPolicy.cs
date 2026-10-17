using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.Client;

internal static class KeyLoadClientStreamPolicy
{
    internal const string SectionName = "KeyLoad:ClientStreaming";
    internal const string Invalid = "A finite native SDK streaming policy is required.";
    private const int MinimumPositiveBytes = 1;

    internal static bool IsValid(CqrsStreamClientOptions value) =>
        value.MaximumFrameBytes is >= MinimumPositiveBytes and <= CqrsStreamClientOptions.MaximumFrameBytesLimit
        && value.MaximumFailureBodyBytes is >= MinimumPositiveBytes and <= CqrsStreamClientOptions.MaximumFailureBodyBytesLimit
        && value.MaximumStreamBytes is >= MinimumPositiveBytes && value.EnsureTerminalChunk
        && value.MalformedChunkBehavior == CqrsMalformedChunkBehavior.EmitFailedChunk;

    internal static CqrsStreamClientOptions Snapshot(IOptions<CqrsStreamClientOptions> configured)
    {
        ArgumentNullException.ThrowIfNull(configured);
        var value = configured.Value;
        if (!IsValid(value))
        {
            throw new OptionsValidationException(SectionName, typeof(CqrsStreamClientOptions), [Invalid]);
        }
        return new CqrsStreamClientOptions
        {
            MaximumFrameBytes = value.MaximumFrameBytes,
            MaximumFailureBodyBytes = value.MaximumFailureBodyBytes,
            MaximumStreamBytes = value.MaximumStreamBytes,
            EnsureTerminalChunk = value.EnsureTerminalChunk,
            AssignSequenceNumbers = value.AssignSequenceNumbers,
            MalformedChunkBehavior = value.MalformedChunkBehavior,
            JsonSerializerOptions = JsonDefaults.Options
        };
    }
}
