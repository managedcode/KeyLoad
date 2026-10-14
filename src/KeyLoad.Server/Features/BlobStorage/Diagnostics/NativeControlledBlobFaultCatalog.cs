using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal static class NativeControlledBlobFaultCatalog
{
    private const string UnsupportedIdentity = "The original controlled blob fault identity is unsupported.";
    private const byte NonemptyNativePayloadByte = 0x01;
    private const long EpochStep = 1;
    private const string WrongNonce = "wire-fault-nonce";
    private const string WrongResource = "wire-fault-resource";

    internal static IEnumerable<NativeControlledBlobFaultPayload> Create(RemoteControlledBlobCall original,
        string originalSignature, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(original);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalSignature);
        if (original.Request?.Frame is not { } frame || !Supported(frame.Purpose, frame.Original?.Kind))
        {
            throw new InvalidOperationException(UnsupportedIdentity);
        }
        foreach (var payload in EnvelopeFaults(original, originalSignature, token))
        {
            yield return payload;
        }
        foreach (var payload in AuthorizationFaults(original, frame, originalSignature, token))
        {
            yield return payload;
        }
        foreach (var payload in OperationFaults(original, frame, originalSignature, token))
        {
            yield return payload;
        }
    }

    private static IEnumerable<NativeControlledBlobFaultPayload> EnvelopeFaults(RemoteControlledBlobCall original,
        string originalSignature, CancellationToken token)
    {
        yield return Wrap(NativeControlledBlobFaultVariation.Nonce, original with
        {
            Nonce = WrongNonce
        }, originalSignature, token);
        yield return Wrap(NativeControlledBlobFaultVariation.RequestId, original with
        {
            RequestId = Guid.NewGuid()
        }, originalSignature, token);
        yield return Wrap(NativeControlledBlobFaultVariation.SourcePhysicalShard,
            original with
            {
                Source = original.Source with
                {
                    PhysicalShardId = Guid.NewGuid()
                }
            }, originalSignature, token);
        yield return Wrap(NativeControlledBlobFaultVariation.DestinationIncarnation, original with
        {
            Destination = original.Destination with
            {
                Owner = original.Destination.Owner with
                {
                    Incarnation = Guid.NewGuid()
                }
            }
        }, originalSignature, token);
        yield return Encode(NativeControlledBlobFaultVariation.MissingEnvelope,
            new RemoteDocumentTransportEnvelope(null, null, null), originalSignature, token);
    }

    private static IEnumerable<NativeControlledBlobFaultPayload> AuthorizationFaults(RemoteControlledBlobCall original, ControlledBlobReadFrame frame,
        string originalSignature, CancellationToken token)
    {
        yield return Wrap(NativeControlledBlobFaultVariation.PrincipalPolicyEpoch, original with
        {
            Request = original.Request with
            {
                Frame = frame with
                {
                    Principal = frame.Principal with
                    {
                        PolicyEpoch = checked(frame.Principal.PolicyEpoch + EpochStep)
                    }
                }
            }
        }, originalSignature, token);
        yield return Wrap(NativeControlledBlobFaultVariation.QueryId, original with
        {
            Request = original.Request with
            {
                Frame = frame with
                {
                    QueryId = Guid.NewGuid()
                }
            }
        }, originalSignature, token);
        yield return Wrap(NativeControlledBlobFaultVariation.Resource, original with
        {
            Request = original.Request with
            {
                Frame = frame with
                {
                    Resource = frame.Resource with
                    {
                        Name = WrongResource
                    }
                }
            }
        }, originalSignature, token);
    }

    private static IEnumerable<NativeControlledBlobFaultPayload> OperationFaults(RemoteControlledBlobCall original, ControlledBlobReadFrame frame,
        string originalSignature, CancellationToken token)
    {
        yield return Wrap(NativeControlledBlobFaultVariation.ExpiresAt, original with
        {
            Request = original.Request with
            {
                Frame = frame with
                {
                    ExpiresAt = DateTimeOffset.MinValue
                }
            }
        }, originalSignature, token);
        yield return Wrap(NativeControlledBlobFaultVariation.Purpose, original with
        {
            Request = original.Request with
            {
                Frame = frame with
                {
                    Purpose = frame.Purpose == ControlledBlobReadPurpose.Outcome ? ControlledBlobReadPurpose.Metadata : ControlledBlobReadPurpose.Outcome
                }
            }
        }, originalSignature, token);
        yield return Wrap(NativeControlledBlobFaultVariation.NativeRequest, original with
        {
            Request = original.Request with
            {
                Frame = frame with
                {
                    NativeRequest = frame.NativeRequest.IsEmpty ? new byte[] { NonemptyNativePayloadByte } : ReadOnlyMemory<byte>.Empty
                }
            }
        }, originalSignature, token);
        if (frame.OriginalOutcome is not null)
        {
            yield return Wrap(NativeControlledBlobFaultVariation.OriginalOutcome, original with
            {
                Request = original.Request with
                {
                    Frame = frame with
                    {
                        OriginalOutcome = null
                    }
                }
            }, originalSignature, token);
        }
        if (frame.Original is { } operation)
        {
            yield return Wrap(NativeControlledBlobFaultVariation.OriginalOperationId, original with
            {
                Request = original.Request with
                {
                    Frame = frame with
                    {
                        Original = operation with
                        {
                            Id = Guid.NewGuid()
                        }
                    }
                }
            }, originalSignature, token);
        }
    }

    private static bool Supported(ControlledBlobReadPurpose purpose, OperationKind? kind)
        => purpose == ControlledBlobReadPurpose.Outcome
            ? kind is OperationKind.BeginBlobUpload or OperationKind.WriteBlobPart or OperationKind.CompleteBlobUpload
                or OperationKind.AbortBlobUpload or OperationKind.DeleteBlob or OperationKind.ReclaimBlob
            : kind is null && purpose is ControlledBlobReadPurpose.Metadata or ControlledBlobReadPurpose.UploadInfo
                or ControlledBlobReadPurpose.Range or ControlledBlobReadPurpose.List;

    private static NativeControlledBlobFaultPayload Wrap(NativeControlledBlobFaultVariation variation,
        RemoteControlledBlobCall call, string signature, CancellationToken token)
        => Encode(variation, new RemoteDocumentTransportEnvelope(null, null, call), signature, token);

    private static NativeControlledBlobFaultPayload Encode(NativeControlledBlobFaultVariation variation,
        RemoteDocumentTransportEnvelope envelope, string signature, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new(variation, RemoteDocumentWire.Encode(envelope), signature);
    }
}
