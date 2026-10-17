using System.Collections.Concurrent;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Features.InternalSerialization;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.Replication;

// Concrete candidate for root-owned pre-admission policy. No owner source installation or runtime
// qualification. Metadata string properties are proxies: callers MUST use Metadata(proxy, bound).
internal static class ReplicaNativeInspection
{
    private static readonly ConcurrentDictionary<Type, Lazy<NativeSerializerContext>> Contexts = new();
    private static readonly HashSet<Type> SupportedRoots =
    [
        typeof(VoteRequest), typeof(AppendRequest), typeof(ReplicatedOperation), typeof(ReplicaEntry),
        typeof(ReplicaEntryBatch), typeof(ReplicaSnapshot), typeof(SnapshotBeginRequest),
        typeof(SnapshotChunkRequest), typeof(SnapshotCompleteRequest), typeof(string),
        typeof(ReplicaHardState), typeof(ReplicaBenchmarkMembershipRecord), typeof(NativeCommandPayload),
        typeof(ReplicaForwardRequestV1), typeof(ReplicaForwardReplyV1)
    ];

    internal static ReplicaInspectedValue<T> Inspect<T>(ReadOnlyMemory<byte> message, int maximumEntries, string? expectedSender = null)
    {
        _ = ReplicaProtocolCodec.PayloadBytes(message.Span);
        return InspectNative<T>(message[ReplicaProtocol.PayloadPrefixBytes..], maximumEntries, expectedSender);
    }

    internal static void Warmup<T>() => _ = Context(typeof(T));

    internal static ReplicaInspectedValue<T> InspectNative<T>(ReadOnlyMemory<byte> input, int maximumEntries,
        string? expectedSender = null)
    {
        if (!SupportedRoots.Contains(typeof(T)))
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaPersistence.InvalidEncoding);
        }
        var context = Context(typeof(T));
        var buffers = new ReplicaInspectionBuffers(input, maximumEntries, expectedSender: expectedSender);
        using var session = context.Sessions.GetSession();
        using var bound = ReplicaInspectionBuffers.Bind(session, buffers);
        var value = NativeSerialization.Deserialize<T>(input.Span, session);
        return new(value, buffers, context);
    }

    private static NativeSerializerContext Context(Type type) => Contexts.GetOrAdd(type, static root => new(() =>
        NativeSerializerProviders.CreateInspection(root, Configure))).Value;

    private static void Configure(ISerializerBuilder builder)
    {
        builder.AddAssembly(typeof(ReplicaEntry).Assembly);
        Register<ReplicaBorrowedStringCodec>(builder);
        Register<ReplicaBorrowedBytesCodec>(builder);
        Register<ReplicaEntryInspectionCodec>(builder);
        Register<ReplicaOperationInspectionCodec>(builder);
        builder.Services.AddSingleton(provider => new ReplicaEntryArrayInspectionCodec(provider.GetRequiredService<ReplicaEntryInspectionCodec>()));
        builder.Services.AddSingleton(provider => new ReplicaVoterArrayInspectionCodec(provider.GetRequiredService<ReplicaBorrowedStringCodec>()));
        builder.Configure(options =>
        {
            options.FieldCodecs.Add(typeof(ReplicaEntryArrayInspectionCodec));
            options.FieldCodecs.Add(typeof(ReplicaVoterArrayInspectionCodec));
        });
        Register<ReplicaEntriesInspectionCodec>(builder);
        Register<ReplicaVotersInspectionCodec>(builder);
        Register<ReplicaEntryBatchInspectionCodec>(builder);
        Register<ReplicaVoteInspectionCodec>(builder);
        Register<ReplicaAppendInspectionCodec>(builder);
        Register<ReplicaSnapshotInspectionCodec>(builder);
        Register<ReplicaSnapshotBeginInspectionCodec>(builder);
        Register<ReplicaSnapshotChunkInspectionCodec>(builder);
        Register<ReplicaSnapshotCompleteInspectionCodec>(builder);
        Register<ReplicaHardStateInspectionCodec>(builder);
        Register<ReplicaMembershipInspectionCodec>(builder);
        Register<ReplicaNativeCommandInspectionCodec>(builder);
        Register<ReplicaForwardRequestInspectionCodec>(builder);
        Register<ReplicaForwardGrantInspectionCodec>(builder);
        Register<ReplicaForwardReplyInspectionCodec>(builder);
        Register<ReplicaForwardStatsInspectionCodec>(builder);
    }

    private static void Register<TCodec>(ISerializerBuilder builder) where TCodec : class
    {
        builder.Services.AddSingleton<TCodec>();
        builder.Configure(options => options.FieldCodecs.Add(typeof(TCodec)));
    }
}
