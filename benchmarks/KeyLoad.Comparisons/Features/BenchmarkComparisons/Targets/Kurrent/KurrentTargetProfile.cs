namespace KeyLoad.Comparisons.Targets;

internal static class KurrentTargetProfile
{
    internal static TargetProfile Create(string connectionString, string image, ComparisonTopology topology)
    {
        const int FirstElementIndex = 0;
        const int SingleItemCount = 1;

        var imageName = image.Split(KurrentConstants.ImageDigestSeparator, KurrentConstants.ImageReferenceParts)[FirstElementIndex];
        var imageTag = imageName.Split(KurrentConstants.ImagePathSeparator).Last().Split(KurrentConstants.ImageTagSeparator).Last();
        if (imageTag != KurrentConstants.ExpectedServerVersion)
        {
            throw new ComparisonFailureException(KurrentConstants.VersionMismatch);
        }

        var settings = KurrentNativeSettings.CreateWriter(connectionString);
        var connectivity = settings.ConnectivitySettings;
        var insecure = connectivity.Insecure;
        var transport = insecure ? KurrentConstants.Insecure : KurrentConstants.TlsVerified;
        var credentialsConfigured = settings.DefaultCredentials is not null;
        var clientCertificateConfigured = connectivity.ClientCertificate is not null;
        var authorization = insecure
            ? credentialsConfigured ? KurrentConstants.InsecureCredentialsIgnored : KurrentConstants.Unauthenticated
            : credentialsConfigured ? KurrentConstants.Authenticated : KurrentConstants.Unauthenticated;
        var certificateMetadata = clientCertificateConfigured
            ? insecure ? KurrentConstants.TlsClientCertificateIgnored : KurrentConstants.TlsClientCertificateConfigured
            : KurrentConstants.TlsClientCertificateAbsent;
        var profile = new TargetProfile(KurrentConstants.Name, KurrentConstants.ExpectedServerVersion,
            ComparisonTopologies.NodeCount(topology) == SingleItemCount ? KurrentConstants.SingleTopology :
                topology == ComparisonTopology.TwoNode ? KurrentConstants.TwoNodeTopology : KurrentConstants.ReplicatedTopology,
            ComparisonTopologies.NodeCount(topology) > SingleItemCount ? KurrentConstants.ReplicatedAcknowledgement : KurrentConstants.SingleAcknowledgement,
            KurrentConstants.ReadContract + KurrentConstants.WriterPreferenceLabel,
            transport, authorization + KurrentConstants.AuthorizationSeparator + certificateMetadata + KurrentConstants.AuthorizationSeparator + KurrentConstants.CommunityAuthorization, image);
        return profile;
    }
}
