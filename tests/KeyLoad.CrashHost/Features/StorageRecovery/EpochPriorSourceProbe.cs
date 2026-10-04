using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class EpochPriorSourceProbe
{
    internal const string Mode = "epoch5-native-probe";
    internal const string SourceRevision = "7784b6b46b98ce994dd98070dc1f58fe4e506b91";
    internal const string CreateOperation = "create";
    internal const string CreateNodeOperation = "createNode";
    internal const string InspectOperation = "inspect";
    internal const string VerifySnapshotOperation = "verifySnapshot";
    private const int ExpectedDataEpoch = 5;
    private const int RejectedExitCode = 1;
    private const int MaximumInputCharacters = 16384;
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
        if (args.Length != 1 || args[0] != Mode)
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
                reply = await ExecuteAsync(request);
            }
            catch (KeyLoadException failure)
            {
                Environment.ExitCode = RejectedExitCode;
                reply = EpochPriorSourceReply.Rejected(failure.Code);
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

    private static async Task<EpochPriorSourceReply> ExecuteAsync(EpochPriorSourceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Directory) || !KnownOperation(request.Operation))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidProbe);
        }
        if (request.Operation is CreateOperation or CreateNodeOperation && Directory.Exists(request.Directory))
        {
            throw Errors.Fail(ErrorCode.Conflict, InvalidProbe);
        }
        if (request.Operation == CreateNodeOperation)
        {
            return await EpochUpgradeFixture.CreateNodeAsync(request.Directory,
                request.NodeProfile ?? throw Errors.Fail(ErrorCode.Validation, InvalidProbe));
        }
        if (request.Operation == VerifySnapshotOperation)
        {
            return VerifySnapshot(request);
        }

        using var store = new ZoneTreeStore(new(request.Directory));
        if (store.Identity.FormatVersion != ExpectedDataEpoch)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, InvalidProbe);
        }
        if (request.Operation == CreateOperation)
        {
            EpochUpgradeFixture.Seed(store, request.Compacted);
        }
        var applied = store.Read(view => NativeSerialization.Deserialize<long>(
            view.ReadOwnedValue(EpochUpgradeFixture.AppliedKey)!));
        return EpochPriorSourceReply.Succeeded(store.Identity, store.Position, applied);
    }

    private static EpochPriorSourceReply VerifySnapshot(EpochPriorSourceRequest request)
    {
        using var input = File.OpenRead(request.Snapshot ?? throw Errors.Fail(ErrorCode.Validation, InvalidProbe));
        var snapshot = ZoneTreeCheckpointReader.Read(input, new ZoneTreeStoreOptions(request.Directory));
        var identity = ZoneTreeIdentityFile.Read(Path.Combine(request.Directory,
            ZoneTreePersistenceFormat.IdentityFileName));
        return EpochPriorSourceReply.Succeeded(identity, snapshot.Position, snapshot.AppliedPosition);
    }

    private static bool KnownOperation(string operation)
        => operation is CreateOperation or CreateNodeOperation or InspectOperation or VerifySnapshotOperation;
}

internal sealed record EpochPriorSourceRequest(string Directory, string Operation, bool Compacted = false,
    string? Snapshot = null, EpochPriorNodeProfile? NodeProfile = null);

internal sealed record EpochPriorSourceReply(string SourceRevision, int DataEpoch, long Position,
    long AppliedPosition, Guid NodeId, Guid Incarnation, long ReadGeneration, bool DispatchPaused,
    string SigningKeySha256, DurabilityProfile Durability, string? ErrorCode = null)
{
    internal static EpochPriorSourceReply Succeeded(StoreIdentity identity, long position, long applied)
        => new(EpochPriorSourceProbe.SourceRevision, identity.FormatVersion, position, applied,
            identity.NodeId, identity.Incarnation, identity.ReadGeneration, identity.DispatchPaused,
            Convert.ToHexStringLower(SHA256.HashData(identity.SigningKey.Span)), identity.Durability);

    internal static EpochPriorSourceReply Rejected(ErrorCode code)
        => new(EpochPriorSourceProbe.SourceRevision, 0, 0, 0, Guid.Empty, Guid.Empty, 0, false,
            string.Empty, DurabilityProfile.ProcessDurable, code.ToString());
}

internal static class EpochPriorProbeInput
{
    private const int ChunkCharacters = 256;
    private const string InvalidInput = "The prior-executable probe request is invalid or exceeds its bound.";

    internal static async Task<EpochPriorSourceRequest> ReadAsync(int maximum, JsonSerializerOptions options)
    {
        var retained = new char[maximum];
        var chunk = new char[ChunkCharacters];
        var length = 0;
        while (true)
        {
            var count = await Console.In.ReadAsync(chunk.AsMemory());
            if (count == 0)
            {
                return JsonSerializer.Deserialize<EpochPriorSourceRequest>(retained.AsSpan(0, length), options)
                    ?? throw new InvalidDataException(InvalidInput);
            }
            if (count > maximum - length)
            {
                throw new InvalidDataException(InvalidInput);
            }
            chunk.AsSpan(0, count).CopyTo(retained.AsSpan(length));
            length += count;
        }
    }
}
