using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chatify.Chat.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring distributed database
/// integration in the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This class contains extension methods that encapsulate
/// the registration of distributed database infrastructure services. This approach keeps
/// the Program.cs clean and provides a single, discoverable location for
/// distributed database configuration.
/// </para>
/// <para>
/// <b>Clean Architecture:</b> This extension lives in the Infrastructure layer
/// and registers infrastructure implementations of the ports defined in the
/// Application layer (e.g., <c>IChatHistoryRepository</c>).
/// </para>
/// <para>
/// <b>Distributed Database Integration:</b> The distributed database is used as the primary data store for
/// chat message history and other application data in Chatify. It provides:
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
/// builder.Services.AddDatabase(
///     builder.Configuration
/// );
/// ]]></code>
/// </para>
/// <para>
/// <b>Configuration Section:</b> By default, this extension reads from the
/// <c>"Chatify:Database"</c> configuration section. Ensure your appsettings.json
/// or environment variables provide the required configuration:
/// <code><![CDATA[
/// {
///   "Chatify": {
///     "Database": {
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
/// <b>Forwarding:</b> This method forwards to <c>AddScyllaChatify</c> for the
/// actual implementation. This allows Program.cs to use the generic <c>AddDatabase</c>
/// method while following the provider-specific naming convention in the implementation.
/// </para>
/// </remarks>
public static class ServiceCollectionDatabaseExtensions
{
    /// <summary>
    /// Registers Chatify distributed database infrastructure services with the dependency
    /// injection container.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to add services to.
    /// Must not be null.
    /// </param>
    /// <param name="configuration">
    /// The application configuration containing distributed database settings.
    /// Must not be null.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance so that multiple
    /// calls can be chained.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Implementation:</b> This method forwards to <see cref="ServiceCollectionScyllaExtensions.AddScyllaChatify"/>
    /// which performs the actual database service registration.
    /// </para>
    /// <para>
    /// <b>Registered Services:</b> The forwarded method registers:
    /// <list type="bullet">
    /// <item><see cref="Options.ScyllaOptionsEntity"/> as a configured options object (singleton)</item>
    /// <item>Cassandra <see cref="Cassandra.ISession"/> for database operations (singleton)</item>
    /// <item>Cassandra <see cref="Cassandra.ICluster"/> for cluster management (singleton)</item>
    /// <item>ChatHistoryRepository implementation of <see cref="Ports.IChatHistoryRepository"/> (singleton)</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Forward to the ScyllaDB-specific extension method
        return services.AddScyllaChatify(configuration);
    }
}
