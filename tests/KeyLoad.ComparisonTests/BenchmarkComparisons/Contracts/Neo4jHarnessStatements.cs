namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class Neo4jHarnessStatements
{
    public static string CreateConstraint(string constraintName, string label) =>
        $"CREATE CONSTRAINT {constraintName} FOR (n:{label}) REQUIRE n.{Neo4jHarnessConstants.IdProperty} IS UNIQUE";

    public static string CreateMarker(string label) =>
        $"CREATE (n:{label} {{{Neo4jHarnessConstants.IdProperty}:${Neo4jHarnessConstants.IdProperty}}}) RETURN n.{Neo4jHarnessConstants.IdProperty}";

    public static string MarkerExists(string label) =>
        $"MATCH (n:{label} {{{Neo4jHarnessConstants.IdProperty}:${Neo4jHarnessConstants.IdProperty}}}) RETURN count(n)";

    public static string DeleteMarker(string label) =>
        $"MATCH (n:{label} {{{Neo4jHarnessConstants.IdProperty}:${Neo4jHarnessConstants.IdProperty}}}) DETACH DELETE n";

    public static string DropConstraint(string constraintName) => $"DROP CONSTRAINT {constraintName} IF EXISTS";

    public static string FindConstraint() =>
        $"SHOW CONSTRAINTS YIELD {Neo4jHarnessConstants.NameProperty} WHERE {Neo4jHarnessConstants.NameProperty}=$name RETURN {Neo4jHarnessConstants.NameProperty}";

    public static string CountNodes(string label) => $"MATCH (n:{label}) RETURN count(n)";

    public static string ReadDocuments(string label) =>
        $"MATCH (n:{label}) WHERE n.{Neo4jHarnessConstants.IdProperty} IN $ids RETURN n.{Neo4jHarnessConstants.IdProperty},n.{Neo4jHarnessConstants.JsonProperty} ORDER BY n.{Neo4jHarnessConstants.IdProperty}";

    public static string UpdateDocuments(string label) =>
        $"UNWIND $documents AS d MATCH (n:{label} {{{Neo4jHarnessConstants.IdProperty}:d.{Neo4jHarnessConstants.IdProperty}}}) SET n.{Neo4jHarnessConstants.JsonProperty}=d.{Neo4jHarnessConstants.JsonProperty}";
}
