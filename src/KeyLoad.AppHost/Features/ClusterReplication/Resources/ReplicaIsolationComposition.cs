using System.Security.Cryptography;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Composes only an explicitly owned three-voter namespace fault cohort.</summary>
internal static class ReplicaIsolationComposition
{
    internal const string Stage = "fault-runtime";
    internal const string Dockerfile = "src/KeyLoad.AppHost/Features/ClusterReplication/Containers/FaultIsolation/Dockerfile";
    private const string DockerfileName = "Dockerfile";
    internal const string BaseArgument = "KEYLOAD_SERVER_IMAGE";
    internal const string SourceArgument = "KEYLOAD_FAULT_SOURCE";
    internal const string Capability = "--cap-add=NET_ADMIN";
    internal const string IncarnationLabel = "keyload.fault.incarnation";
    private const string LabelArgument = "--label=";
    private const string DigestMarker = "@sha256:";
    private const int DigestLength = 64;
    private const int VoterCount = 3;
    private const int SingleContextFile = 1;
    private const int FirstContextFile = 0;
    private const string FirstNode = "node1";
    private const string SecondNode = "node2";
    private const string ThirdNode = "node3";
    private const string IncarnationFormat = "D";
    private const string LabelAssignment = "=";
    private const char DecimalDigitFirst = '0';
    private const char DecimalDigitLast = '9';
    private const char LowerHexDigitFirst = 'a';
    private const char LowerHexDigitLast = 'f';
    private const string ImmutableBaseRequired = "The namespace fault image requires an authenticated immutable base.";
    private const string OriginalBaseMismatch = "The configured original server reference differs from its authenticated source receipt.";
    private const string ExactVotersRequired = "The namespace fault cohort must own exactly three named voters.";
    private const string MissingContext = "The fault Dockerfile context is absent.";
    private const string ContextFilePattern = "*";
    private const string ExactContextRequired = "The immutable fault build context must contain only its owned Dockerfile.";
    private const string BuildTargetMismatch = "The fault build must retain the exact admitted base and a distinct native build target.";
    private static readonly string[] Names = [FirstNode, SecondNode, ThirdNode];

    internal static async Task<ReplicaIsolationBuildPlan> ApplyAsync(IDistributedApplicationBuilder builder, string repository, string baseImage, Guid incarnation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (incarnation == Guid.Empty || !baseImage.Contains(DigestMarker, StringComparison.Ordinal)
            || baseImage[(baseImage.IndexOf(DigestMarker, StringComparison.Ordinal) + DigestMarker.Length)..].Length != DigestLength
            || !baseImage[(baseImage.IndexOf(DigestMarker, StringComparison.Ordinal) + DigestMarker.Length)..]
                .All(character => character is >= DecimalDigitFirst and <= DecimalDigitLast or >= LowerHexDigitFirst and <= LowerHexDigitLast))
        { throw new InvalidOperationException(ImmutableBaseRequired); }
        var admitted = RuntimeContainerImage.Read(builder, RuntimeContainerImage.ServerConfiguration);
        if (admitted.Reference != baseImage)
        { throw new InvalidOperationException(OriginalBaseMismatch); }
        var nativeBaseImage = admitted.Image + DigestMarker + admitted.Digest;
        var nodes = builder.Resources.OfType<ContainerResource>().ToArray();
        if (nodes.Length != VoterCount || !nodes.Select(node => node.Name).Order(StringComparer.Ordinal).SequenceEqual(Names))
        { throw new InvalidOperationException(ExactVotersRequired); }
        var dockerfile = Path.Combine(repository, Dockerfile);
        var context = Path.GetDirectoryName(dockerfile) ?? throw new InvalidOperationException(MissingContext);
        var contextFiles = Directory.EnumerateFiles(context, ContextFilePattern, SearchOption.AllDirectories).ToArray();
        if (contextFiles.Length != SingleContextFile || contextFiles[FirstContextFile] != dockerfile)
        { throw new InvalidOperationException(ExactContextRequired); }
        var source = await File.ReadAllBytesAsync(dockerfile, cancellationToken).ConfigureAwait(false);
        var digest = Convert.ToHexStringLower(SHA256.HashData(source));
        var targets = new List<ReplicaIsolationBuildTarget>();
        foreach (var node in nodes)
        {
            var serviceUser = await ReplicaIsolationRuntimeUser.ReadAsync(node, cancellationToken).ConfigureAwait(false);
            builder.CreateResourceBuilder(node).WithDockerfile(context, DockerfileName, Stage)
                .WithBuildArg(BaseArgument, baseImage).WithBuildArg(SourceArgument, digest)
                .WithContainerRuntimeArgs(Capability, LabelArgument + IncarnationLabel + LabelAssignment + incarnation.ToString(IncarnationFormat));
            if (!node.TryGetContainerImageName(useBuiltImage: true, out var builtImage)
                || !node.TryGetContainerImageName(useBuiltImage: false, out var originalImage)
                || !string.Equals(originalImage, nativeBaseImage, StringComparison.Ordinal)
                || string.Equals(builtImage, baseImage, StringComparison.Ordinal))
            { throw new InvalidOperationException(BuildTargetMismatch); }
            targets.Add(new(node.Name, builtImage, serviceUser));
        }
        return new(baseImage, nativeBaseImage, digest, context, incarnation, targets.ToArray());
    }
}
