namespace Chatify.Chat.Infrastructure.Options;

/// <summary>
/// Configuration options for Redis caching and pub/sub integration in Chatify.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This options class encapsulates all configuration required to connect
/// to and interact with a Redis server for caching, presence tracking, rate limiting,
/// and pub/sub messaging in the Chatify system.
/// </para>
/// <para>
/// <b>Configuration Binding:</b> These options are bound from the IConfiguration
/// instance provided to the DI container. The typical configuration section is
/// "Chatify:Redis". Example appsettings.json:
/// <code><![CDATA[
/// {
///   "Chatify": {
///     "Redis": {
///       "ConnectionString": "localhost:6379"
///     }
///   }
/// }
/// ]]></code>
/// </para>
/// <para>
/// <b>Connection String Formats:</b> The connection string can be specified in
/// multiple formats depending on the Redis configuration:
/// <list type="bullet">
/// <item><c>localhost:6379</c> - Simple host and port</item>
/// <item><c>redis.example.com:6379</c> - Hostname and port</item>
/// <item><c>localhost:6379,password=secret</c> - With password</item>
/// <item><c>localhost:6379,ssl=true</c> - With SSL/TLS</item>
/// <item><c>localhost:6379,password=secret,ssl=true,defaultDatabase=1</c> - Full configuration</item>
/// </list>
/// </para>
/// <para>
/// <b>Use Cases in Chatify:</b>
/// <list type="bullet">
/// <item><b>Presence Tracking:</b> Store online/offline status and connection IDs for users</item>
/// <item><b>Rate Limiting:</b> Track request counts per user within sliding windows</item>
/// <item><b>Pub/Sub:</b> Broadcast real-time events across multiple pod instances</item>
/// <item><b>Caching:</b> Cache frequently accessed data to reduce database load</item>
/// </list>
/// </para>
/// <para>
/// <b>Validation:</b> These options are validated when registered via DI.
/// The connection string must not be null or whitespace.
/// </para>
/// <para>
/// <b>Redis Stack:</b> Redis Stack (formerly Redis Modules) is compatible and can
/// be used for advanced features like JSON storage, search, and probabilistic
/// data structures if needed in the future.
/// </para>
/// </remarks>
public record RedisOptionsEntity
{
    /// <summary>
    /// Gets the connection string for connecting to the Redis server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Required:</b> This field must not be null or whitespace.
    /// </para>
    /// <para>
    /// <b>Format:</b> The connection string uses a comma-separated key=value format.
    /// Common configuration options include:
    /// <list type="bullet">
    /// <item><c>host:port</c> or just <c>host</c> - Redis server address (required)</item>
    /// <item><c>password=value</c> - Redis password (AUTH)</item>
    /// <item><c>ssl=true/false</c> - Enable SSL/TLS encryption</item>
    /// <item><c>defaultDatabase=n</c> - Redis database number (0-15)</item>
    /// <item><c>abortConnect=true/false</c> - Fail if connection cannot be established</item>
    /// <item><c>connectTimeout=n</c> - Connection timeout in milliseconds</item>
    /// <item><c>syncTimeout=n</c> - Synchronous operation timeout in milliseconds</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Examples:</b>
    /// <list type="bullet">
    /// <item><c>localhost:6379</c> - Local development</item>
    /// <item><c>redis.prod.example.com:6379,ssl=true,password=xyz123</c> - Production with SSL</item>
    /// <item><c>redis-cluster.example.com:6379,password=secret,defaultDatabase=1</c> - With auth and DB</item>
    /// <item><c>localhost:6379,abortConnect=false,connectRetry=3</c> - With retry logic</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Redis Sentinel:</b> For high availability with Sentinel, use the format:
    /// <c>sentinel1:26379,sentinel2:26379,sentinel3:26379,serviceName=mymaster</c>
    /// </para>
    /// <para>
    /// <b>Redis Cluster:</b> For clustered deployments, specify multiple nodes:
    /// <c>node1:6379,node2:6379,node3:6379</c>
    /// </para>
    /// <para>
    /// <b>Kubernetes:</b> When using a Redis Helm chart or operator, the connection
    /// string typically references a service: <c>redis-master.chatify.svc.cluster.local:6379</c>
    /// </para>
    /// </remarks>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// Validates the Redis options configuration.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the connection string is present and valid; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method performs the following validations:
    /// <list type="bullet">
    /// <item><see cref="ConnectionString"/> is not null or whitespace</item>
    /// </list>
    /// </para>
    /// <para>
    /// This method is called by the DI extension when registering Redis services.
    /// If validation fails, an <see cref="ArgumentException"/> is thrown during
    /// service registration to fail fast before the application starts.
    /// </para>
    /// <para>
    /// <b>Note:</b> This method does not attempt to connect to the Redis server.
    /// Connection validation occurs when the first Redis operation is performed
    /// or when health checks run.
    /// </para>
    /// </remarks>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns a string representation of the Redis options for logging purposes.
    /// </summary>
    /// <returns>
    /// A string indicating whether a connection string is configured, excluding sensitive data.
    /// </returns>
    /// <remarks>
    /// This method is useful for logging the Redis configuration on startup without
    /// exposing sensitive connection details like passwords. It only indicates whether
    /// a connection string is present.
    /// </remarks>
    public override string ToString()
    {
        bool hasConnectionString = !string.IsNullOrWhiteSpace(ConnectionString);
        return $"RedisOptionsEntity {{ ConnectionString configured = {hasConnectionString} }}";
    }
}
