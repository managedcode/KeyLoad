namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedQuorumResourceTokens
{
    internal const string Qdrant = "Qdrant";
    internal const string Rabbit = "RabbitMQ";
    internal const string Profile = "intensive-1k-c16";
    internal const string Runner = "isolated-model-runner";
    internal const string ModelImage = "model-runner";
    internal const string TemporaryPrefix = "keyload-isolated-quorum-model-";
    internal const string NativeDirectory = "native";
    internal const string NativePrefix = "Benchmarks__Native__";
    internal const string Endpoints = NativePrefix + "Endpoints__";
    internal const string Image = NativePrefix + "Image";
    internal const string ApiKey = NativePrefix + "ApiKey";
    internal const string User = NativePrefix + "User";
    internal const string Password = NativePrefix + "Password";
    internal const string Connection = NativePrefix + "ConnectionString";
    internal const string QdrantPrefix = "isolated-qdrant-";
    internal const string RabbitPrefix = "isolated-rabbit-";
    internal const string Http = "http";
    internal const string Management = "management";
    internal const string ClusterEnabled = "QDRANT__CLUSTER__ENABLED";
    internal const string QdrantKey = "QDRANT__SERVICE__API_KEY";
    internal const string QdrantEntrypoint = "/qdrant/entrypoint.sh";
    internal const string QdrantImage = "docker.io/qdrant/qdrant:v1.17.1@";
    internal const string RabbitImage = "docker.io/library/rabbitmq:4.2.4-management@";
    internal const string UriPrefix = "http://";
    internal const string PeerPort = ":6335";
    internal const string False = "false";
    internal const string True = "true";
    internal const string QdrantStorage = "/qdrant/storage";
    internal const string RabbitStorage = "/var/lib/rabbitmq";
    internal const string Cookie = "RABBITMQ_ERLANG_COOKIE";
    internal const string NodeName = "RABBITMQ_NODENAME";
    internal const string LongName = "RABBITMQ_USE_LONGNAME";
    internal const string RabbitUser = "RABBITMQ_DEFAULT_USER";
    internal const string RabbitPassword = "RABBITMQ_DEFAULT_PASS";
    internal const string RabbitConfig = "/etc/rabbitmq/isolated-cluster.conf";
    internal const string RabbitConfigSetting = "RABBITMQ_CONFIG_FILE";
    internal const string ConfigStem = "/etc/rabbitmq/isolated-cluster";
    internal const string Discovery = "cluster_formation.peer_discovery_backend = classic_config";
    internal const string Disc = "cluster_formation.node_type = disc";
    internal const string PeerPrefix = "cluster_formation.classic_config.nodes.";
    internal const string RabbitNamePrefix = "rabbit@";
    internal const string UriFlag = "--uri";
    internal const string BootstrapFlag = "--bootstrap";
    internal const string PeerOne = "http://isolated-qdrant-1:6335";
}
