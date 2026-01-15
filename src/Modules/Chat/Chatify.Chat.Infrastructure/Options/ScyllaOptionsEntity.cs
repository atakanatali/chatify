namespace Chatify.Chat.Infrastructure.Options;

/// <summary>
/// Configuration options for ScyllaDB/Cassandra distributed database integration in Chatify.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This options class encapsulates all configuration required to connect
/// to and interact with a ScyllaDB or Apache Cassandra distributed database for persisting
/// chat message history and other application data in the Chatify system.
/// </para>
/// <para>
/// <b>Why ScyllaDB:</b> ScyllaDB is a Cassandra-compatible distributed database that provides:
/// <list type="bullet">
/// <item>Linear scalability with no single point of failure</item>
/// <item>Low and predictable latency at scale</item>
/// <item>High write throughput (ideal for chat message append-heavy workloads)</item>
/// <item>Tunable consistency for different data access patterns</item>
/// <item>CQL (Cassandra Query Language) for familiar SQL-like queries</item>
/// </list>
/// </para>
/// <para>
/// <b>Configuration Binding:</b> These options are bound from the IConfiguration
/// instance provided to the DI container. The typical configuration section is
/// "Chatify:Scylla". Example appsettings.json:
/// <code><![CDATA[
/// {
///   "Chatify": {
///     "Scylla": {
///       "ContactPoints": "scylla-node1:9042,scylla-node2:9042,scylla-node3:9042",
///       "Keyspace": "chatify",
///       "Username": "chatify_user",
///       "Password": "secure_password"
///     }
///   }
/// }
/// ]]></code>
/// </para>
/// <para>
/// <b>Keyspace Strategy:</b> The keyspace should be created with a replication strategy
/// appropriate for the deployment. For production:
/// <list type="bullet">
/// <item><c>NetworkTopologyStrategy</c> with replication factor of 3 (multi-DC)</item>
/// <item><c>SimpleStrategy</c> with replication factor of 3 (single DC)</item>
/// </list>
/// </para>
/// <para>
/// <b>Data Modeling:</b> Chatify uses ScyllaDB for chat message storage with tables
/// optimized for query patterns by scope and time range. Messages are partitioned by
/// <c>(ScopeType, ScopeId, Bucket)</c> for efficient retrieval.
/// </para>
/// <para>
/// <b>Validation:</b> These options are validated when registered via DI.
/// Required fields include ContactPoints and Keyspace.
/// </para>
/// </remarks>
public record ScyllaOptionsEntity
{
    /// <summary>
    /// Gets the comma-separated list of ScyllaDB/Cassandra node contact points in the format
    /// <c>host1:port1,host2:port2,...</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Required:</b> This field must not be null or whitespace.
    /// </para>
    /// <para>
    /// <b>Format:</b> Each contact point consists of a hostname or IP address
    /// followed by a port number. The native transport port is 9042 by default.
    /// Multiple nodes are separated by commas.
    /// </para>
    /// <para>
    /// <b>Examples:</b>
    /// <list type="bullet">
    /// <item><c>localhost:9042</c> - Single local node</item>
    /// <item><c>scylla-node1.example.com:9042,scylla-node2.example.com:9042,scylla-node3.example.com:9042</c> - Multiple nodes</item>
    /// <item><c>192.168.1.20:9042,192.168.1.21:9042,192.168.1.22:9042</c> - IP addresses</item>
    /// <item><c>scylla-db.chatify.svc.cluster.local:9042</c> - Kubernetes service</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Driver Behavior:</b> The C# driver will use all contact points to discover
    /// the cluster topology. Providing multiple contact points improves reliability
    /// during startup if one node is unavailable.
    /// </para>
    /// <para>
    /// <b>Load Balancing:</b> The driver automatically distributes requests across
    /// all nodes in the cluster using a token-aware policy that routes requests to
    /// the appropriate replicas based on the partition key.
    /// </para>
    /// </remarks>
    public string ContactPoints { get; init; } = string.Empty;

    /// <summary>
    /// Gets the name of the Cassandra keyspace used for Chatify data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Required:</b> This field must not be null or whitespace.
    /// </para>
    /// <para>
    /// <b>Keyspace:</b> A keyspace is a namespace that defines data replication on nodes.
    /// It's similar to a database in relational databases.
    /// </para>
    /// <para>
    /// <b>Naming Conventions:</b> Keyspace names should be lowercase and use
    /// underscores or hyphens. Avoid special characters and spaces.
    /// </para>
    /// <para>
    /// <b>Examples:</b> <c>chatify</c>, <c>chatify_prod</c>, <c>chatify_chat</c>
    /// </para>
    /// <para>
    /// <b>Keyspace Creation:</b> The keyspace should be created before Chatify starts.
    /// Example CQL:
    /// <code><![CDATA[
    /// CREATE KEYSPACE IF NOT EXISTS chatify
    /// WITH REPLICATION = {
    ///   'class': 'NetworkTopologyStrategy',
    ///   'dc1': 3,
    ///   'dc2': 3
    /// } AND DURABLE_WRITES = true;
    /// ]]></code>
    /// </para>
    /// <para>
    /// <b>Development:</b> For local development, you can use SimpleStrategy:
    /// <code><![CDATA[
    /// CREATE KEYSPACE IF NOT EXISTS chatify
    /// WITH REPLICATION = {
    ///   'class': 'SimpleStrategy',
    ///   'replication_factor': 1
    /// };
    /// ]]></code>
    /// </para>
    /// </remarks>
    public string Keyspace { get; init; } = string.Empty;

    /// <summary>
    /// Gets the username for authenticating with the ScyllaDB/Cassandra cluster.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Optional:</b> This field may be null or whitespace if authentication is not
    /// enabled or is handled differently (e.g., via environment-specific mechanisms).
    /// </para>
    /// <para>
    /// <b>Authentication:</b> If specified, the driver will use PLAIN text authentication
    /// with the provided username and password. For production deployments, ensure
    /// SSL/TLS is enabled to encrypt credentials in transit.
    /// </para>
    /// <para>
    /// <b>User Management:</b> In Cassandra/ScyllaDB, users are created with CQL:
    /// <code><![CDATA[
    /// CREATE USER IF NOT EXISTS 'chatify_user' WITH PASSWORD 'secure_password' NOSUPERUSER;
    /// GRANT ALL PERMISSIONS ON KEYSPACE chatify TO 'chatify_user';
    /// ]]></code>
    /// </para>
    /// <para>
    /// <b>Principle of Least Privilege:</b> The application user should only have
    /// permissions on the Chatify keyspace and should not be a superuser in production.
    /// </para>
    /// </remarks>
    public string? Username { get; init; }

    /// <summary>
    /// Gets the password for authenticating with the ScyllaDB/Cassandra cluster.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Optional:</b> This field may be null or whitespace if authentication is not
    /// enabled or is handled differently.
    /// </para>
    /// <para>
    /// <b>Security:</b> Passwords should be stored securely using:
    /// <list type="bullet">
    /// <item>Kubernetes secrets</item>
    /// <item>Key Vault / Secrets Manager</item>
    /// <item>Environment variables (with proper access controls)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Never Hardcode:</b> Avoid hardcoding passwords in appsettings.json or
    /// source control. Use configuration providers that inject secrets at runtime.
    /// </para>
    /// <para>
    /// <b>Rotation:</b> Implement password rotation procedures for production deployments.
    /// </para>
    /// </remarks>
    public string? Password { get; init; }

    /// <summary>
    /// Validates the ScyllaDB options configuration.
    /// </summary>
    /// <returns>
    /// <c>true</c> if all required fields are present and valid; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method performs the following validations:
    /// <list type="bullet">
    /// <item><see cref="ContactPoints"/> is not null or whitespace</item>
    /// <item><see cref="Keyspace"/> is not null or whitespace</item>
    /// <item>If <see cref="Username"/> is provided, <see cref="Password"/> must also be provided (and vice versa)</item>
    /// </list>
    /// </para>
    /// <para>
    /// This method is called by the DI extension when registering ScyllaDB services.
    /// If validation fails, an <see cref="ArgumentException"/> is thrown during
    /// service registration to fail fast before the application starts.
    /// </para>
    /// </remarks>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(ContactPoints))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Keyspace))
        {
            return false;
        }

        // If username is provided, password must also be provided
        if (!string.IsNullOrWhiteSpace(Username) && string.IsNullOrWhiteSpace(Password))
        {
            return false;
        }

        // If password is provided, username must also be provided
        if (!string.IsNullOrWhiteSpace(Password) && string.IsNullOrWhiteSpace(Username))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns a string representation of the ScyllaDB options for logging purposes.
    /// </summary>
    /// <returns>
    /// A string containing the key configuration properties, excluding sensitive data.
    /// </returns>
    /// <remarks>
    /// This method is useful for logging the ScyllaDB configuration on startup without
    /// exposing sensitive credentials. It includes the contact points and keyspace,
    /// but only indicates whether authentication is configured (not the actual credentials).
    /// </remarks>
    public override string ToString()
    {
        bool hasAuth = !string.IsNullOrWhiteSpace(Username);
        return $"ScyllaOptionsEntity {{ ContactPoints = {ContactPoints}, Keyspace = {Keyspace}, Authentication configured = {hasAuth} }}";
    }
}
