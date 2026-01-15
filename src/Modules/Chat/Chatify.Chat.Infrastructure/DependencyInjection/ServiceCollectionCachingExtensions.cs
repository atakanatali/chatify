using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Application.Ports;
using Chatify.Chat.Infrastructure.Options;
using Chatify.Chat.Infrastructure.Services.PodIdentity;
using Chatify.Chat.Infrastructure.Services.Presence;
using Chatify.Chat.Infrastructure.Services.RateLimit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chatify.Chat.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring distributed caching
/// integration in the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This class contains extension methods that encapsulate
/// the registration of distributed cache infrastructure services. This approach keeps
/// the Program.cs clean and provides a single, discoverable location for
/// distributed cache configuration.
/// </para>
/// <para>
/// <b>Clean Architecture:</b> This extension lives in the Infrastructure layer
/// and registers infrastructure implementations of the ports defined in the
/// Application layer (e.g., <c>IPresenceService</c>, <c>IRateLimitService</c>).
/// </para>
/// <para>
/// <b>Distributed Cache Integration:</b> The distributed cache is used for multiple purposes in Chatify:
/// <list type="bullet">
/// <item><b>Presence Tracking:</b> Store online/offline status and connection IDs</item>
/// <item><b>Rate Limiting:</b> Track request counts per user within sliding windows</item>
/// <item><b>Pub/Sub:</b> Broadcast real-time events across multiple pod instances</item>
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
    /// The application configuration containing distributed cache settings.
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
    /// Thrown when distributed cache configuration is invalid or missing required fields.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Registered Services:</b> This method registers:
    /// <list type="bullet">
    /// <item><see cref="RedisOptionsEntity"/> as a configured options object (singleton)</item>
    /// <item>Distributed cache implementation of <see cref="IPresenceService"/> (singleton)</item>
    /// <item>Distributed cache implementation of <see cref="IRateLimitService"/> (singleton)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Options Binding:</b> The method binds configuration from the
    /// <c>"Chatify:Caching"</c> section to <see cref="RedisOptionsEntity"/> and
    /// validates the connection string before registration.
    /// </para>
    /// <para>
    /// <b>Validation:</b> The following validations are performed:
    /// <list type="bullet">
    /// <item><see cref="RedisOptionsEntity.ConnectionString"/> must not be empty</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Service Lifetimes:</b>
    /// <list type="bullet">
    /// <item>Distributed cache options: Singleton (configuration is read-only)</item>
    /// <item>Presence service: Singleton (stateless service that uses the cache)</item>
    /// <item>Rate limit service: Singleton (stateless service that uses the cache)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Current Implementation:</b> This method currently performs configuration
    /// binding and validation, and registers placeholder service implementations.
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

        services.AddSingleton(cachingOptions);
        services.AddSingleton<IPresenceService, PresenceService>();
        services.AddSingleton<IRateLimitService, RateLimitService>();

        // Register pod identity service (no configuration required, reads from environment)
        services.AddSingleton<IPodIdentityService, PodIdentityService>();

        return services;
    }
}
