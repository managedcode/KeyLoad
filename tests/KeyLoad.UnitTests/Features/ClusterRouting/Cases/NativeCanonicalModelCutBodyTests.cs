using System.IO.Pipelines;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeCanonicalModelCutBodyTests
{
    private const int OverflowByte = 1;
    private const long UnreadPosition = 0;
    private const int ResumeDivisor = 2;

    [Test]
    public async Task ActualGeneratedRequestBodyRoundTripsAllOriginalFields()
    {
        var request = NativeCanonicalModelCutAdmissionFixture.Request();
        var bytes = NativeSerialization.Serialize(request);
        using var body = new MemoryStream(bytes, writable: false);
        var http = HttpRequest(body);
        var decoded = await NativeCanonicalModelCutBody.ReadAsync(http, CancellationToken.None);
        await Assert.That(decoded).IsEqualTo(request);
        await Assert.That(NativeSerialization.Serialize(decoded).SequenceEqual(bytes)).IsTrue();
        await Assert.That(body.Position).IsEqualTo(body.Length);
    }

    [Test]
    public async Task QueryEncodingAndWrongContentTypeRefuseBeforeReadingOriginalBody()
    {
        var bytes = NativeSerialization.Serialize(NativeCanonicalModelCutAdmissionFixture.Request());
        using var body = new MemoryStream(bytes, writable: false);
        var query = HttpRequest(body);
        query.QueryString = new("?unexpected=true");
        await RequireCodeAsync(query, ErrorCode.Validation);
        await Assert.That(body.Position).IsEqualTo(UnreadPosition);
        var encoded = HttpRequest(body);
        encoded.Headers.ContentEncoding = "gzip";
        await RequireCodeAsync(encoded, ErrorCode.Validation);
        await Assert.That(body.Position).IsEqualTo(UnreadPosition);
        var json = HttpRequest(body);
        json.ContentType = "application/json";
        await RequireCodeAsync(json, ErrorCode.Validation);
        await Assert.That(body.Position).IsEqualTo(UnreadPosition);
    }

    [Test]
    public async Task ExactBoundedMalformedBodyAndOverflowRefuseWithoutTruncation()
    {
        using var bounded = new MemoryStream(new byte[NativeCanonicalModelCutCodec.MaximumRecordBytes], writable: false);
        await RequireCodeAsync(HttpRequest(bounded), ErrorCode.Corruption);
        await Assert.That(bounded.Position).IsEqualTo(bounded.Length);
        using var overflow = new MemoryStream(new byte[NativeCanonicalModelCutCodec.MaximumRecordBytes + OverflowByte], writable: false);
        var unknownLength = HttpRequest(overflow);
        unknownLength.ContentLength = null;
        await RequireCodeAsync(unknownLength, ErrorCode.ResourceExhausted);
        await Assert.That(overflow.Position).IsEqualTo(overflow.Length);
        overflow.Position = UnreadPosition;
        await RequireCodeAsync(HttpRequest(overflow), ErrorCode.ResourceExhausted);
        await Assert.That(overflow.Position).IsEqualTo(UnreadPosition);
    }

    [Test]
    public async Task WrongGeneratedBodyKindAndActualCancellationNeverProduceARequest()
    {
        var request = NativeCanonicalModelCutAdmissionFixture.Request();
        using var wrong = new MemoryStream(NativeSerialization.Serialize(request.SourcePartition), writable: false);
        await RequireCodeAsync(HttpRequest(wrong), ErrorCode.Corruption);
        using var original = new MemoryStream(NativeSerialization.Serialize(request), writable: false);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var http = HttpRequest(original);
        http.HttpContext.RequestAborted = cancellation.Token;
        var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            NativeCanonicalModelCutBody.ReadAsync(http, http.HttpContext.RequestAborted));
        await Assert.That(error!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(original.Position).IsEqualTo(UnreadPosition);
    }

    [Test]
    public async Task ActualPendingPipeStreamReadCancelsAndOriginalTaskJoinsBeforePipeCleanup()
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: NativeCanonicalModelCutCodec.MaximumRecordBytes,
            resumeWriterThreshold: NativeCanonicalModelCutCodec.MaximumRecordBytes / ResumeDivisor,
            useSynchronizationContext: false));
        using var cancellation = new CancellationTokenSource();
        await using var stream = pipe.Reader.AsStream(leaveOpen: true);
        var http = new DefaultHttpContext().Request;
        http.ContentType = NativeCanonicalModelCutDiagnosticProtocol.ContentType;
        http.Body = stream;
        var failures = new List<Exception>();
        Task<NativeCanonicalModelCutRequest>? original = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            original = NativeCanonicalModelCutBody.ReadAsync(http, cancellation.Token);
            await cancellation.CancelAsync();
            var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => original!);
            await Assert.That(error!.CancellationToken).IsEqualTo(cancellation.Token);
        }, failures);
        if (original is not null)
        {
            await ((Task)original).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (original.Exception is { } fault)
            {
                failures.AddRange(fault.InnerExceptions);
            }
        }
        await ServerFailureObserver.ObserveAsync(() => pipe.Writer.CompleteAsync().AsTask(), failures);
        await ServerFailureObserver.ObserveAsync(() => pipe.Reader.CompleteAsync().AsTask(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static HttpRequest HttpRequest(MemoryStream body)
    {
        var request = new DefaultHttpContext().Request;
        request.ContentType = NativeCanonicalModelCutDiagnosticProtocol.ContentType;
        request.ContentLength = body.Length;
        request.Body = body;
        return request;
    }

    private static async Task RequireCodeAsync(HttpRequest request, ErrorCode code)
    {
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            NativeCanonicalModelCutBody.ReadAsync(request, CancellationToken.None));
        await Assert.That(error!.Code).IsEqualTo(code);
    }
}
