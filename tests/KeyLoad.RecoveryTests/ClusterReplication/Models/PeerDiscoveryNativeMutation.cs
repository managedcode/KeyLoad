namespace KeyLoad.RecoveryTests;

internal enum PeerDiscoveryNativeMutation
{
    Version, Caller, CallerAddress, Destination, DestinationAddress, Nonce, Expiry, Signature,
    MissingCaller, MissingCallerAddress, MissingDestination, MissingDestinationAddress, MissingSignature,
    DuplicateSignature, OversizedCaller, MalformedSignature
}

