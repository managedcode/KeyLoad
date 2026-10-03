# Pinned Kurrent SDK routing source review

TASK-NGR-K3R / REQ-NGR-003, read-only source evidence under Accepted ADR-068.
NuGet metadata binds KurrentDB.Client1.4.0 to official commit
`e041cf8f459d6abe05f39918ecd62a71038b9ddc`. This source review does not prove
the destination or cause of an original failed call, or an SDK defect.

The pinned [connection parser](https://github.com/kurrent-io/KurrentDB-Client-Dotnet/blob/e041cf8f459d6abe05f39918ecd62a71038b9ddc/src/KurrentDB.Client/Core/KurrentDBClientSettings.ConnectionString.cs)
accepts the configured multi-host esdb scheme and leader preference.
[GossipChannelSelector](https://github.com/kurrent-io/KurrentDB-Client-Dotnet/blob/e041cf8f459d6abe05f39918ecd62a71038b9ddc/src/KurrentDB.Client/Core/GossipChannelSelector.cs)
uses the first successful gossip response; the
[node selector](https://github.com/kurrent-io/KurrentDB-Client-Dotnet/blob/e041cf8f459d6abe05f39918ecd62a71038b9ddc/src/KurrentDB.Client/Core/NodeSelector.cs)
ranks available members. Leader preference can fall back to a follower when
that response contains no usable leader. This is a possible mechanism, not
evidence that it caused either preserved KeyLoad failure.

[TypedExceptionInterceptor](https://github.com/kurrent-io/KurrentDB-Client-Dotnet/blob/e041cf8f459d6abe05f39918ecd62a71038b9ddc/src/KurrentDB.Client/Core/Interceptors/TypedExceptionInterceptor.cs)
maps the NotLeader trailer key and leader-host/port trailers to the typed
exception with its original RpcException. Its actual path does not invoke the
separate Google RPC detail parser in NotLeaderException.FromRpcException.
The exception exposes a public DnsEndPoint LeaderEndpoint.
[ReportLeaderInterceptor](https://github.com/kurrent-io/KurrentDB-Client-Dotnet/blob/e041cf8f459d6abe05f39918ecd62a71038b9ddc/src/KurrentDB.Client/Core/Interceptors/ReportLeaderInterceptor.cs)
reports that endpoint for future channel selection; raw Unavailable errors
instead request rediscovery. The
[batch append receiver](https://github.com/kurrent-io/KurrentDB-Client-Dotnet/blob/e041cf8f459d6abe05f39918ecd62a71038b9ddc/src/KurrentDB.Client/Streams/KurrentDBClient.Append.cs)
faults the original pending append and replaces its appender for later calls.
No transparent replay of that same failed append was found.

The [7d985 n3 original](sql-client-qualification-7d985.json) and
[7d1196 n2 original](native-kurrent-preflight-failure-7d1196.json) both retain
NoStreamSemantics/NotLeaderException, all five null measurements and clean
teardown. Identical stage/type does not prove identical causes. Their sanitized
exports omit the selected destination, reported LeaderEndpoint value, gRPC
status code and SDK's own selected gossip snapshot. Cause remains unknown;
no consumer workaround, SDK repair or routing change is justified by this review.

The next real native proof must correlate the original selected/receiving node,
reported leader and status-code-only diagnostic with contemporaneous all-node
roles. Any added capture needs a bounded redacted contract before implementation.
Preserve the failed original; success still requires unchanged NoStream ACK,
expected conflict and exact original-event readback on actual1/2/3 members.
Do not export messages, raw trailers, credentials, endpoints or user payloads,
or retry the failed append to make its qualification green.

Research report SHA-256
`49b3fe94a528e942db05fbcef6c75a82ac40649f9d1b203f6c9f052a8f4a73e3`;
source inventory SHA-256
`890d18e3f8ae1119afc4401d7da0ae4ceec62ffa552822f179683b1e4a686d51`.
The inventory records immutable Git SHA-1 blob IDs, not a claimed local SHA-256
of retrieved source. There was no package installation, runtime, native SDK
execution or repository mutation by the research worker. This record is manual
architecture evidence and cannot qualify performance or the complete cohort.
