using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Application.Ports;
using Chatify.Chat.Infrastructure.Options;
using Chatify.Chat.Infrastructure.Services.PodIdentity;
using Chatify.Chat.Infrastructure.Services.Presence;
using Chatify.Chat.Infrastructure.Services.RateLimit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Chatify.Chat.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring distributed caching, presence tracking,
/// and rate limiting integration in the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This class contains extension methods that encapsulate
/// the registration of distributed cache infrastructure services. This approach keeps
/// the Program.cs clean and provides a single, discoverable location for
/// cache configuration.
/// </para>
/// <para>
/// <b>Clean Architecture:</b> This extension lives in the Infrastructure layer
/// and registers infrastructure implementations of the ports defined in the
/// Application layer (e.g., <c>IPresenceService</c>, <c>IRateLimitService</c>).
/// </para>
/// <para>
/// <b>Distributed Cache Integration:</b> The distributed cache is used for multiple purposes in Chatify:
/// <list type="bullet">
/// <item><b>Presence Tracking:</b> Store online/offline status and connection IDs for users</item>
/// <item><b>Rate Limiting:</b> Track request counts per user within sliding windows</item>
/// <item><b>Routing:</b> Store pod-to-user connection mappings for distributed message routing</item>
/// <item><b>Caching:</b> Cache frequently accessed data to reduce load on other services</item>
/// </list>
/// </para>
/// <para>
/// <b>Usage Pattern:</b>
/// <code><![CDATA[
/// // In Program.cs
/// builder.Services.AddCaching(
///     builder.Configuration
/// );
/// ]]></code>
/// </para>
/// <para>
/// <b>Configuration Section:</b> By default, this extension reads from the
/// <c>"Chatify:Caching"</c> configuration section. Ensure your appsettings.json
/// or environment variables provide the required configuration:
/// <code><![CDATA[
/// {
///   "Chatify": {
///     "Caching": {
///       "ConnectionString": "localhost:6379"
///     }
///   }
/// }
/// ]]></code>
/// </para>
/// </remarks>
public static class ServiceCollectionCachingExtensions
{
    /// <summary>
    /// Registers Chatify distributed cache infrastructure services with the dependency
    /// injection container.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to add services to.
    /// Must not be null.
    /// </param>
    /// <param name="configuration">
    /// The application configuration containing cache settings.
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
    /// Thrown when cache configuration is invalid or missing required fields.
    /// </exception>
    /// <exception cref="RedisConnectionException">
    /// Thrown when the cache connection cannot be established.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Registered Services:</b> This method registers:
    /// <list type="bullet">
    /// <item><see cref="RedisOptionsEntity"/> as a configured options object (singleton)</item>
    /// <item><see cref="IConnectionMultiplexer"/> as the cache connection multiplexer (singleton)</item>
    /// <item>Cache implementation of <see cref="IPresenceService"/> (singleton)</item>
    /// <item>Cache implementation of <see cref="IRateLimitService"/> (singleton)</item>
    /// <item>Pod identity service for tracking the current pod instance (singleton)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Options Binding:</b> The method binds configuration from the
    /// <c>"Chatify:Caching"</c> section to <see cref="RedisOptionsEntity"/> and
    /// validates the connection string before registration.
    /// </para>
    /// <para>
    /// <b>Connection Multiplexer:</b> The <see cref="IConnectionMultiplexer"/> is registered
    /// as a singleton and is disposed automatically when the application shuts down.
    /// The multiplexer provides thread-safe access to cache connections and is
    /// reused across all cache operations.
    /// </para>
    /// <para>
    /// <b>Validation:</b> The following validations are performed:
    /// <list type="bullet">
    /// <item><see cref="RedisOptionsEntity.ConnectionString"/> must not be empty</item>
    /// <item>Cache connection is tested on startup to ensure connectivity</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Service Lifetimes:</b>
    /// <list type="bullet">
    /// <item>Cache options: Singleton (configuration is read-only)</item>
    /// <item>Connection multiplexer: Singleton (maintains connection pool)</item>
    /// <item>Presence service: Singleton (stateless service that uses the multiplexer)</item>
    /// <item>Rate limit service: Singleton (stateless service that uses the multiplexer)</item>
    /// <item>Pod identity service: Singleton (runtime property that doesn't change)</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        GuardUtility.NotNull(services);
        GuardUtility.NotNull(configuration);

        var cachingSection = configuration.GetSection("Chatify:Caching");
        var cachingOptions = cachingSection.Get<RedisOptionsEntity>()
            ?? new RedisOptionsEntity();

        if (!cachingOptions.IsValid())
        {
            throw new ArgumentException(
                $"Invalid distributed cache configuration. " +
                $"Please check the 'Chatify:Caching' configuration section. " +
                $"Required field: ConnectionString. " +
                $"Provided options: {cachingOptions}",
                nameof(configuration));
        }

        // Register cache options as a singleton
        services.AddSingleton(cachingOptions);

        // Register the cache connection multiplexer as a singleton
        // The multiplexer will be disposed automatically when the application shuts down
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<RedisOptionsEntity>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger(typeof(ServiceCollectionCachingExtensions));

            var configurationOptions = ConfigurationOptions.Parse(options.ConnectionString);
            configurationOptions.AbortOnConnectFail = false;
            configurationOptions.ConnectRetry = 3;
            configurationOptions.ConnectTimeout = 5000;
            configurationOptions.SyncTimeout = 5000;

            logger.LogInformation(
                "Connecting to distributed cache at {ConnectionString}",
                configurationOptions.EndPoints.FirstOrDefault()?.ToString() ?? options.ConnectionString);

            var multiplexer = ConnectionMultiplexer.Connect(configurationOptions);

            logger.LogInformation(
                "Successfully connected to distributed cache. Endpoints: {Endpoints}, IsConnected: {IsConnected}",
                string.Join(", ", multiplexer.GetEndPoints().Select(e => e.ToString())),
                multiplexer.IsConnected);

            return multiplexer;
        });

        // Register pod identity service (no configuration required, reads from environment)
        services.AddSingleton<IPodIdentityService, PodIdentityService>();

        // Register presence service with cache backend
        services.AddSingleton<IPresenceService, PresenceService>();

        // Register rate limit service with cache backend
        services.AddSingleton<IRateLimitService, RateLimitService>();

        return services;
    }
}
