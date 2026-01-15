using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Application.Ports;
using Chatify.Chat.Infrastructure.Options;
using Chatify.Chat.Infrastructure.Services.ChatHistory;
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
/// builder.Services.AddDistributedDatabase(
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
/// </remarks>
public static class ServiceCollectionScyllaExtensions
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
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when distributed database configuration is invalid or missing required fields.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Registered Services:</b> This method registers:
    /// <list type="bullet">
    /// <item><see cref="ScyllaOptionsEntity"/> as a configured options object (singleton)</item>
    /// <item>Distributed database implementation of <see cref="IChatHistoryRepository"/> (singleton)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Options Binding:</b> The method binds configuration from the
    /// <c>"Chatify:Scylla"</c> section to <see cref="ScyllaOptionsEntity"/> and
    /// validates all required fields before registration.
    /// </para>
    /// <para>
    /// <b>Validation:</b> The following validations are performed:
    /// <list type="bullet">
    /// <item><see cref="ScyllaOptionsEntity.ContactPoints"/> must not be empty</item>
    /// <item><see cref="ScyllaOptionsEntity.Keyspace"/> must not be empty</item>
    /// <item>If <see cref="ScyllaOptionsEntity.Username"/> is provided, <see cref="ScyllaOptionsEntity.Password"/> must also be provided</item>
    /// <item>If <see cref="ScyllaOptionsEntity.Password"/> is provided, <see cref="ScyllaOptionsEntity.Username"/> must also be provided</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Service Lifetimes:</b>
    /// <list type="bullet">
    /// <item>Distributed database options: Singleton (configuration is read-only)</item>
    /// <item>Repository services: Singleton (stateless, use the shared session)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Current Implementation:</b> This method currently performs configuration
    /// binding and validation, and registers placeholder service implementations.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddDistributedDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        GuardUtility.NotNull(services);
        GuardUtility.NotNull(configuration);

        var scyllaSection = configuration.GetSection("Chatify:Scylla");
        var scyllaOptions = scyllaSection.Get<ScyllaOptionsEntity>()
            ?? new ScyllaOptionsEntity();

        if (!scyllaOptions.IsValid())
        {
            throw new ArgumentException(
                $"Invalid distributed database configuration. " +
                $"Please check the 'Chatify:Scylla' configuration section. " +
                $"Required fields: ContactPoints, Keyspace. " +
                $"Provided options: {scyllaOptions}",
                nameof(configuration));
        }

        services.AddSingleton(scyllaOptions);
        services.AddSingleton<IChatHistoryRepository, ChatHistoryRepository>();

        return services;
    }
}
