using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class EpochPriorSourceProbe
{
    internal const string Mode = "epoch5-native-probe";
    internal const string SourceRevision = "7784b6b46b98ce994dd98070dc1f58fe4e506b91";
    internal const string Native6Mode = "epoch6-native-probe";
    internal const string Native6SourceRevision = "2801b03091efc5cf45b1268c6570457539f12f27";
    internal const string CreateOperation = "create";
    internal const string CreateNodeOperation = "createNode";
    internal const string InspectOperation = "inspect";
    internal const string VerifySnapshotOperation = "verifySnapshot";
    internal const string CreateOutcomeFrameOperation = "createOutcomeFrame";
    private const int ExpectedDataEpoch = 5;
    private const int Native6DataEpoch = 6;
    private const int RejectedExitCode = 1;
    private const int MaximumInputCharacters = 16384;
    internal const int MaximumOutcomeFrameBytes = 16384;
    internal const int MaximumOutcomeFrameBase64Characters = 21848;
    private const string InvalidProbe = "The prior-executable probe input or data epoch is invalid.";
    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    internal static async Task RunAsync(string[] args)
    {
        if (!await TryRunAsync(args))
        {
            await CrashHostApplication.RunAsync(args);
        }
    }

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        const int EmptyArgsLength = 1;
        const int ArgsFirstIndex = 0;

        if (args.Length != EmptyArgsLength || args[ArgsFirstIndex] is not (Mode or Native6Mode))
        {
            return false;
        }

        var output = Console.Out;
        var errors = Console.Error;
        try
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
            var request = await EpochPriorProbeInput.ReadAsync(MaximumInputCharacters, JsonOptions);
            EpochPriorSourceReply reply;
            try
            {
                reply = await ExecuteAsync(request, args[ArgsFirstIndex] == Mode ? ExpectedDataEpoch : Native6DataEpoch);
            }
            catch (KeyLoadException failure)
            {
                Environment.ExitCode = RejectedExitCode;
                reply = EpochPriorSourceReply.Rejected(failure.Code) with
                { SourceRevision = SourceRevisionForEpoch(args[ArgsFirstIndex] == Mode ? ExpectedDataEpoch : Native6DataEpoch) };
            }
            await output.WriteLineAsync(JsonSerializer.Serialize(reply, JsonOptions));
            await output.FlushAsync();
        }
        finally
        {
            Console.SetOut(output);
            Console.SetError(errors);
        }
        return true;
    }

    internal static string SourceRevisionForEpoch(int epoch) => epoch switch
    {
        ExpectedDataEpoch => SourceRevision,
        Native6DataEpoch => Native6SourceRevision,
        _ => throw new ArgumentOutOfRangeException(nameof(epoch))
    };

    internal static string ModeForEpoch(int epoch) => epoch switch
    {
        ExpectedDataEpoch => Mode,
        Native6DataEpoch => Native6Mode,
        _ => throw new ArgumentOutOfRangeException(nameof(epoch))
    };

    private static async Task<EpochPriorSourceReply> ExecuteAsync(EpochPriorSourceRequest request, int expectedDataEpoch)
    {
        ValidateRequest(request);
        if (request.Operation == CreateNodeOperation)
        {
            var reply = await EpochUpgradeFixture.CreateNodeAsync(request.Directory,
                request.NodeProfile ?? throw Errors.Fail(ErrorCode.Validation, InvalidProbe));
            if (reply.DataEpoch != expectedDataEpoch)
            {
                throw Errors.Fail(ErrorCode.FormatUnsupported, InvalidProbe);
            }
            return reply with { SourceRevision = SourceRevisionForEpoch(expectedDataEpoch) };
        }
        if (request.Operation == VerifySnapshotOperation)
        {
            return VerifySnapshot(request) with { SourceRevision = SourceRevisionForEpoch(expectedDataEpoch) };
        }
        if (request.Operation == CreateOutcomeFrameOperation && expectedDataEpoch != Native6DataEpoch)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, InvalidProbe);
        }

        using var store = new ZoneTreeStore(new(request.Directory), CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        if (store.Identity.FormatVersion != expectedDataEpoch)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, InvalidProbe);
        }
        if (request.Operation == CreateOperation)
        {
            EpochUpgradeFixture.Seed(store, request.Compacted);
        }
        if (request.Operation == CreateOutcomeFrameOperation)
        {
            return CreateOutcomeFrame(store, request) with
            { SourceRevision = SourceRevisionForEpoch(expectedDataEpoch) };
        }
        var applied = store.Read(view => NativeSerialization.Deserialize<long>(
            view.ReadOwnedValue(EpochUpgradeFixture.AppliedKey)!));
        return EpochPriorSourceReply.Succeeded(store.Identity, store.Position, applied) with
        { SourceRevision = SourceRevisionForEpoch(expectedDataEpoch) };
    }

    private static void ValidateRequest(EpochPriorSourceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Directory) || !KnownOperation(request.Operation))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidProbe);
        }
        if (request.Operation == CreateOutcomeFrameOperation
            && (request.OutcomeCommandId is not { } commandId || commandId == Guid.Empty))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidProbe);
        }
        if ((request.Operation is CreateOperation or CreateNodeOperation or CreateOutcomeFrameOperation)
            && Directory.Exists(request.Directory))
        {
            throw Errors.Fail(ErrorCode.Conflict, InvalidProbe);
        }
    }

    private static EpochPriorSourceReply CreateOutcomeFrame(ZoneTreeStore store, EpochPriorSourceRequest request)
    {
        const int BytesLengthEmptyCount = 0;

        var commandId = request.OutcomeCommandId!.Value;
        var bytes = EpochUpgradeFixture.CreateOutcomeFrame(store, commandId);
        if (bytes.Length is <= BytesLengthEmptyCount or > MaximumOutcomeFrameBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidProbe);
        }
        var encoded = Convert.ToBase64String(bytes);
        if (encoded.Length > MaximumOutcomeFrameBase64Characters)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidProbe);
        }
        var applied = store.Read(view => NativeSerialization.Deserialize<long>(
            view.ReadOwnedValue(EpochUpgradeFixture.AppliedKey)!));
        return EpochPriorSourceReply.Succeeded(store.Identity, store.Position, applied) with
        {
            OutcomeFrameBase64 = encoded,
            OutcomeCommandId = commandId,
            OutcomePrincipalId = EpochUpgradeFixture.OutcomePrincipalId
        };
    }

    private static EpochPriorSourceReply VerifySnapshot(EpochPriorSourceRequest request)
    {
        var executionOptions = CrashExecutionOptions.StorageExecution();
        var configured = executionOptions.Value;
        var descriptor = new ZoneTreeStoreOptions(request.Directory).ResolveExecutionOptions(executionOptions);
        using var input = new FileStream(request.Snapshot ?? throw Errors.Fail(ErrorCode.Validation, InvalidProbe),
            FileMode.Open, FileAccess.Read, FileShare.Read, configured.StreamBufferBytes);
        var snapshot = ZoneTreeCheckpointReader.Read(input, descriptor);
        var identity = ZoneTreeIdentityFile.Read(Path.Combine(request.Directory,
            ZoneTreePersistenceFormat.IdentityFileName), configured.MaximumIdentityFileBytes, configured.StreamBufferBytes);
        return EpochPriorSourceReply.Succeeded(identity, snapshot.Position, snapshot.AppliedPosition);
    }

    private static bool KnownOperation(string operation)
        => operation is CreateOperation or CreateNodeOperation or InspectOperation or VerifySnapshotOperation
            or CreateOutcomeFrameOperation;
}

internal sealed record EpochPriorSourceRequest(string Directory, string Operation, bool Compacted = false,
    string? Snapshot = null, EpochPriorNodeProfile? NodeProfile = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? OutcomeCommandId = null);

internal sealed record EpochPriorSourceReply(string SourceRevision, int DataEpoch, long Position,
    long AppliedPosition, Guid NodeId, Guid Incarnation, long ReadGeneration, bool DispatchPaused,
    string SigningKeySha256, DurabilityProfile Durability, string? ErrorCode = null)
{
    private const int RejectedDataEpochEmptyCount = 0;
    private const int RejectedPositionEmptyCount = 0;
    private const int RejectedAppliedPositionEmptyCount = 0;
    private const int RejectedReadGenerationEmptyCount = 0;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutcomeFrameBase64 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? OutcomeCommandId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutcomePrincipalId { get; init; }

    internal static EpochPriorSourceReply Succeeded(StoreIdentity identity, long position, long applied)
        => new(EpochPriorSourceProbe.SourceRevision, identity.FormatVersion, position, applied,
            identity.NodeId, identity.Incarnation, identity.ReadGeneration, identity.DispatchPaused,
            Convert.ToHexStringLower(SHA256.HashData(identity.SigningKey.Span)), identity.Durability);

    internal static EpochPriorSourceReply Rejected(ErrorCode code)
        => new(EpochPriorSourceProbe.SourceRevision, RejectedDataEpochEmptyCount, RejectedPositionEmptyCount, RejectedAppliedPositionEmptyCount, Guid.Empty, Guid.Empty, RejectedReadGenerationEmptyCount, false,
            string.Empty, DurabilityProfile.ProcessDurable, code.ToString());
}

internal static class EpochPriorProbeInput
{
    private const string InvalidInput = "The prior-executable probe request is invalid or exceeds its bound.";

    internal static async Task<EpochPriorSourceRequest> ReadAsync(int maximum, JsonSerializerOptions options)
    {
        const int LengthInitialValue = 0;
        const int EmptyCount = 0;
        const int StartEmptyCount = 0;

        var retained = new char[maximum];
        var chunk = new char[CrashExecutionOptions.Child().Value.ProfileReadChunkCharacters];
        var length = LengthInitialValue;
        while (true)
        {
            var count = await Console.In.ReadAsync(chunk.AsMemory());
            if (count == EmptyCount)
            {
                return JsonSerializer.Deserialize<EpochPriorSourceRequest>(retained.AsSpan(StartEmptyCount, length), options)
                    ?? throw new InvalidDataException(InvalidInput);
            }
            if (count > maximum - length)
            {
                throw new InvalidDataException(InvalidInput);
            }
            chunk.AsSpan(StartEmptyCount, count).CopyTo(retained.AsSpan(length));
            length += count;
        }
    }
}
