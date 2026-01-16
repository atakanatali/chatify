using Cassandra;
using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Application.Ports;
using Chatify.Chat.Infrastructure.Options;
using Chatify.Chat.Infrastructure.Services.ChatHistory;
using Chatify.Chat.Infrastructure.Services.ChatHistory.Cql;
using Chatify.Chat.Infrastructure.Services.ChatHistory.Mapping;
using Chatify.Chat.Infrastructure.Services.ChatHistory.Scoping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Chatify.Chat.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring ScyllaDB/Cassandra integration
/// in the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This class contains extension methods that encapsulate
/// the registration of ScyllaDB infrastructure services. This approach keeps
/// the Program.cs clean and provides a single, discoverable location for
/// ScyllaDB configuration.
/// </para>
/// <para>
/// <b>Clean Architecture:</b> This extension lives in the Infrastructure layer
/// and registers infrastructure implementations of the ports defined in the
/// Application layer (e.g., <c>IChatHistoryRepository</c>).
/// </para>
/// <para>
/// <b>ScyllaDB Integration:</b> ScyllaDB is used as the primary data store for
/// chat message history in Chatify. It provides:
/// <list type="bullet">
/// <item>Linear scalability with no single point of failure</item>
/// <item>Low and predictable latency at scale</item>
/// <item>High write throughput (ideal for chat message append-heavy workloads)</item>
/// <item>Tunable consistency for different data access patterns</item>
/// <item>CQL (Cassandra Query Language) for familiar SQL-like queries</item>
/// </list>
/// </para>
/// <para>
/// <b>Usage Pattern:</b>
/// <code><![CDATA[
/// // In Program.cs
/// builder.Services.AddScyllaChatify(
///     builder.Configuration
/// );
/// ]]></code>
/// </para>
/// <para>
/// <b>Configuration Section:</b> By default, this extension reads from the
/// <c>"Chatify:Scylla"</c> configuration section. Ensure your appsettings.json
/// or environment variables provide the required configuration:
/// <code><![CDATA[
/// {
///   "Chatify": {
///     "Scylla": {
///       "ContactPoints": "scylla-node1:9042,scylla-node2:9042",
///       "Keyspace": "chatify",
///       "Username": "chatify_user",
///       "Password": "secure_password"
///     }
///   }
/// }
/// ]]></code>
/// </para>
/// <para>
/// <b>Session Lifecycle:</b> The ScyllaDB session is created as a singleton
/// and disposed when the application shuts down. The session manages connection
/// pooling to the ScyllaDB cluster and is thread-safe for concurrent use.
/// </para>
/// </remarks>
public static class ServiceCollectionScyllaExtensions
{
    /// <summary>
    /// Registers Chatify ScyllaDB infrastructure services with the dependency
    /// injection container.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to add services to.
    /// Must not be null.
    /// </param>
    /// <param name="configuration">
    /// The application configuration containing ScyllaDB settings.
    /// Must not be null.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance so that multiple
    /// calls can be chained.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when ScyllaDB configuration is invalid or missing required fields.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Registered Services:</b> This method registers:
    /// <list type="bullet">
    /// <item><see cref="ScyllaOptionsEntity"/> as a configured options object (singleton)</item>
    /// <item><see cref="ISession"/> as a Cassandra/ScyllaDB session (singleton)</item>
    /// <item><see cref="ICluster"/> as a Cassandra/ScyllaDB cluster (singleton)</item>
    /// <item><see cref="IScopeKeySerializer"/> for composite key serialization (singleton)</item>
    /// <item><see cref="ICqlStatementProvider"/> for prepared statement caching (singleton)</item>
    /// <item><see cref="IChatEventRowMapper"/> for row-to-DTO mapping (singleton)</item>
    /// <item><see cref="IChatHistoryRepository"/> for chat history persistence (singleton)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Architecture:</b> The registration follows Clean Architecture principles:
    /// <list type="bullet">
    /// <item>Infrastructure components depend on abstractions (interfaces)</item>
    /// <item>Repository implements the Application layer port</item>
    /// <item>All dependencies are injected for testability</item>
    /// <item>Statement provider and mapper are reusable across repositories</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddScyllaChatify(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        GuardUtility.NotNull(services);
        GuardUtility.NotNull(configuration);

        // Bind configuration from the "Chatify:Scylla" section first
        // Fall back to "Chatify:Database" for backward compatibility
        var scyllaSection = configuration.GetSection("Chatify:Scylla");
        var scyllaOptions = scyllaSection.Get<ScyllaOptionsEntity>();

        // If "Chatify:Scylla" is not configured, try "Chatify:Database" for backward compatibility
        if (scyllaOptions == null || !scyllaOptions.IsValid())
        {
            var databaseSection = configuration.GetSection("Chatify:Database");
            scyllaOptions = databaseSection.Get<ScyllaOptionsEntity>();
        }

        scyllaOptions ??= new ScyllaOptionsEntity();

        // Validate the configuration
        if (!scyllaOptions.IsValid())
        {
            throw new ArgumentException(
                $"Invalid ScyllaDB configuration. " +
                $"Please check the 'Chatify:Scylla' or 'Chatify:Database' configuration section. " +
                $"Required fields: ContactPoints, Keyspace. " +
                $"Provided options: {scyllaOptions}",
                nameof(configuration));
        }

        // Register the options as a singleton
        services.AddSingleton(scyllaOptions);

        // Build and connect to the cluster
        var cluster = BuildCluster(scyllaOptions);
        services.AddSingleton(cluster);

        // Create and connect to the session (keyspace)
        var session = cluster.Connect(scyllaOptions.Keyspace);
        services.AddSingleton(session);

        // Register infrastructure services (reusable across repositories)
        services.AddSingleton<IScopeKeySerializer, ColonScopeKeySerializer>();
        services.AddSingleton<ICqlStatementProvider, CqlStatementProvider>();
        services.AddSingleton<IChatEventRowMapper, ChatEventRowMapper>();

        // Register the repository implementation with all dependencies
        services.AddSingleton<IChatHistoryRepository, ChatHistoryRepository>();

        return services;
    }

    /// <summary>
    /// Builds and connects a Cassandra/ScyllaDB cluster from the provided options.
    /// </summary>
    /// <param name="options">
    /// The ScyllaDB configuration options. Must not be null.
    /// </param>
    /// <returns>
    /// A connected <see cref="ICluster"/> instance.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="options"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when cluster connection fails.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Builder Configuration:</b> This method configures the cluster builder with:
    /// <list type="bullet">
    /// <item>Contact points parsed from the comma-separated list</item>
    /// <item>Plain text authentication if username/password are provided</item>
    /// <item>Default query options for consistency and retry behavior</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Contact Point Format:</b> The contact points string should be in the format:
    /// <c>host1:port1,host2:port2,...</c>
    /// </para>
    /// <para>
    /// <b>Example:</b>
    /// <code><![CDATA[
    /// var cluster = BuildCluster(new ScyllaOptionsEntity
    /// {
    ///     ContactPoints = "scylla-node1:9042,scylla-node2:9042",
    ///     Keyspace = "chatify",
    ///     Username = "chatify_user",
    ///     Password = "secure_password"
    /// });
    /// ]]></code>
    /// </para>
    /// </remarks>
    private static ICluster BuildCluster(ScyllaOptionsEntity options)
    {
        GuardUtility.NotNull(options);

        // Parse contact points from the comma-separated list
        var contactPoints = options.ContactPoints
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(cp =>
            {
                // If no port specified, use default ScyllaDB port (9042)
                if (!cp.Contains(':'))
                {
                    return $"{cp}:9042";
                }
                return cp;
            })
            .ToArray();

        if (contactPoints.Length == 0)
        {
            throw new InvalidOperationException(
                "No valid contact points found in ScyllaDB configuration.");
        }

        // Build the cluster
        var builder = Cluster.Builder()
            .AddContactPoints(contactPoints)
            .WithDefaultKeyspace(options.Keyspace);

        // Configure authentication if username and password are provided
        if (!string.IsNullOrWhiteSpace(options.Username) &&
            !string.IsNullOrWhiteSpace(options.Password))
        {
            builder.WithCredentials(
                options.Username,
                options.Password);
        }

        // Configure query options for optimal performance
        builder
            .WithQueryOptions(new QueryOptions()
                .SetConsistencyLevel(ConsistencyLevel.LocalQuorum) // Default consistency for queries
                .SetSerialConsistencyLevel(ConsistencyLevel.LocalSerial) // For lightweight transactions
            );

        // Connect to the cluster
        var cluster = builder.Build();
        return cluster;
    }
}
