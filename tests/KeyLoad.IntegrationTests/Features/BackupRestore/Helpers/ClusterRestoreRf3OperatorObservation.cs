using System.Collections.Immutable;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Server;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Owns the original bounded native resource reader; never completes the producer itself.</summary>
internal sealed class ClusterRestoreRf3OperatorObservation
{
    private const int SingleReceiptLine = 1;
    private const string ProblemTypePrefix = "urn:keyload:error:";
    private const string VectorDetail = "The captured partition vector crosses different movement states or omits its effective owner data cut.";
    private const string ProvenanceDetail = "The configured cluster restore vector or node binding is invalid.";
    private const string CredentialDetail = "The credential is unavailable or expired.";
    private readonly List<LogLine> original = [];
    private readonly int maximumCharacters;
    private readonly Task read;
    private int retainedCharacters;
    private bool joined;

    internal ClusterRestoreRf3OperatorObservation(ResourceLoggerService logs, int maximumCharacters,
        CancellationToken cancellationToken)
    {
        this.maximumCharacters = maximumCharacters;
        read = ReadAsync(logs, cancellationToken);
    }

    internal async Task JoinAsync()
    {
        await read.ConfigureAwait(false);
        joined = true;
    }

    internal async Task RequireAsync(Guid captureId, ImmutableArray<ClusterRestoreRf3OperatorNode> actualNodes)
    {
        await Assert.That(joined).IsTrue();
        await Assert.That(original.Count).IsEqualTo(SingleReceiptLine);
        await Assert.That(original.Single().IsErrorMessage).IsFalse();
        var observed = ClusterRestoreRf3LogFraming.Read<ClusterRestoreRf3OperatorReceipt>(original.Single());
        var expected = new ClusterRestoreRf3OperatorReceipt(captureId, actualNodes, observed.ActualElapsed);
        await Assert.That(JsonDefaults.Serialize(observed).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(observed.ActualElapsed).IsGreaterThanOrEqualTo(TimeSpan.Zero);
        await Assert.That(observed.ActualElapsed).IsLessThan(ClusterRestoreRf3Protocol.ParentDeadline);
        await TestContext.Current!.OutputWriter.WriteLineAsync(original.Single().Content);
    }

    internal async Task RequireFailureAsync(ErrorCode expected, string? exactDetail = null)
    {
        await Assert.That(joined).IsTrue();
        await Assert.That(original.Count).IsEqualTo(SingleReceiptLine);
        await Assert.That(original.Single().IsErrorMessage).IsTrue();
        var problem = ClusterRestoreRf3LogFraming.Read<Problem>(original.Single());
        var detail = expected switch
        {
            ErrorCode.RecoveryRequired => VectorDetail,
            ErrorCode.Unauthenticated => CredentialDetail,
            ErrorCode.Corruption => ProvenanceDetail,
            ErrorCode.Conflict => ClusterRestoreRf3ResumeProtocol.PlanMismatch,
            ErrorCode.FormatUnsupported => ClusterRestoreRf3ResumeProtocol.StateCorruption,
            _ => throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid)
        };
        var status = expected switch
        {
            ErrorCode.RecoveryRequired => System.Net.HttpStatusCode.ServiceUnavailable,
            ErrorCode.Unauthenticated => System.Net.HttpStatusCode.Unauthorized,
            ErrorCode.Corruption => System.Net.HttpStatusCode.BadRequest,
            ErrorCode.Conflict => System.Net.HttpStatusCode.Conflict,
            ErrorCode.FormatUnsupported => System.Net.HttpStatusCode.BadRequest,
            _ => throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid)
        };
        var literal = new Problem
        {
            Type = ProblemTypePrefix + expected,
            Title = expected.ToString(),
            ErrorCode = expected.ToString(),
            Detail = exactDetail ?? detail,
            StatusCode = (int)status
        };
        await Assert.That(JsonDefaults.Serialize(problem).SequenceEqual(JsonDefaults.Serialize(literal))).IsTrue();
        await TestContext.Current!.OutputWriter.WriteLineAsync(original.Single().Content);
    }

    internal async Task<ClusterRestoreRf3OperatorReceipt> ReadOriginalReceiptAsync()
    {
        await Assert.That(joined).IsTrue();
        await Assert.That(original.Count).IsEqualTo(SingleReceiptLine);
        await Assert.That(original.Single().IsErrorMessage).IsFalse();
        return ClusterRestoreRf3LogFraming.Read<ClusterRestoreRf3OperatorReceipt>(original.Single());
    }

    internal async Task RequireReplayAsync(ClusterRestoreRf3OperatorReceipt expected)
    {
        var actual = await ReadOriginalReceiptAsync().ConfigureAwait(false);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    private async Task ReadAsync(ResourceLoggerService logs, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var reader = logs.WatchAsync(ClusterRestoreRf3Protocol.RestoreResource).GetAsyncEnumerator(cancellationToken);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            while (await reader.MoveNextAsync().ConfigureAwait(false))
            {
                foreach (var line in reader.Current)
                {
                    if (line.Content.Length > maximumCharacters - retainedCharacters)
                    { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
                    retainedCharacters += line.Content.Length;
                    original.Add(line);
                }
            }
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => reader.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
