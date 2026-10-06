using KeyLoad.AppHost.Hosting;
using System.Globalization;
using System.Security.Cryptography;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedNeo4jResources
{
    private const string GetImageReferenceComparisonText = "@";

    private const string Target = "Neo4j";
    private const string Name = "neo4j";
    private const string Version = "2026.09.0";
    private const string Image = "neo4j";
    private const string ImagePrefix = "docker.io/library/neo4j:";
    private const string PasswordParameter = "neo4j-password";
    private const string AuthenticationEnvironment = "NEO4J_AUTH";
    private const string HttpEndpoint = "http";
    private const string DataMount = "/data";
    private const string ReadyPath = "/";
    private const string UserSetting = "User";
    private const string PasswordSetting = "Password";
    private const string UserArgument = "--user";
    private const string InitialHeapEnvironment = "NEO4J_server_memory_heap_initial__size";
    private const string MaximumHeapEnvironment = "NEO4J_server_memory_heap_max__size";
    private const string PageCacheEnvironment = "NEO4J_server_memory_pagecache_size";
    private const string MegabyteUnit = "m";
    private const string UnsupportedSelection = "IsolatedNeo4jSelectionInvalid";
    private const int SecretBytes = 32;
    private const int HttpPort = 7474;

    internal static string ImageReference => ImagePrefix + Version + GetImageReferenceComparisonText + BenchmarkResources.Neo4jDigest;

    internal static void Add(IsolatedResourceContext context)
    {
        const int SupportedNodeCount = 1;
        const int ElementIndex = 7;
        const int IndexValue = 0;

        ArgumentNullException.ThrowIfNull(context);
        context.Selection.Validate();
        if (context.Selection.Target != Target || context.Selection.NodeCount != SupportedNodeCount)
        {
            throw new InvalidOperationException(UnsupportedSelection);
        }
        var deployment = AppHostOptionsRegistration.Get(context.Builder).Deployment.Value;
        var directory = context.DataDirectory(Name);
        ClusterProfileStore.PrepareDirectory(directory);
        var password = context.Builder.AddParameter(PasswordParameter,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes)), secret: true);
        var node = context.Builder.AddContainer(Name, Image, Version)
            .WithImageSHA256(BenchmarkResources.Neo4jDigest[ElementIndex..]).WithContainerNetworkAlias(Name)
            .WithBindMount(directory, DataMount).WithHttpEndpoint(targetPort: HttpPort, name: HttpEndpoint)
            .WithEnvironment(AuthenticationEnvironment, ReferenceExpression.Create($"{Name}{ReadyPath}{password}"))
            .WithEnvironment(InitialHeapEnvironment, deployment.Neo4jInitialHeapMegabytes.ToString(CultureInfo.InvariantCulture) + MegabyteUnit).WithEnvironment(MaximumHeapEnvironment, deployment.Neo4jMaximumHeapMegabytes.ToString(CultureInfo.InvariantCulture) + MegabyteUnit)
            .WithEnvironment(PageCacheEnvironment, deployment.Neo4jPageCacheMegabytes.ToString(CultureInfo.InvariantCulture) + MegabyteUnit).WithHttpHealthCheck(ReadyPath);
        if (ClusterContainerUser.Resolve(context.Builder) is { } user)
        {
            node.WithContainerRuntimeArgs(UserArgument, user);
        }
        context.BindEndpoint(IndexValue, node, HttpEndpoint);
        context.BindSetting(UserSetting, Name);
        context.BindSetting(PasswordSetting, password);
        context.BindImage(ImageReference);
    }
}
