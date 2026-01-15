using Chatify.BuildingBlocks.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Chatify.BuildingBlocks.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring logging integration
/// in the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This class contains extension methods that encapsulate
/// the configuration of centralized log aggregation via Serilog and Elasticsearch.
/// This approach keeps the Program.cs clean and provides a single, discoverable
/// location for logging configuration.
/// </para>
/// <para>
/// <b>Location:</b> This extension is placed in BuildingBlocks rather than the
/// Observability module because:
/// <list type="bullet">
/// <item>Logging is a fundamental cross-cutting primitive like CorrelationId and Clock</item>
/// <item>It's used across all modules and layers</item>
/// <item>It should have no dependencies on other modules</item>
/// <item>It aligns with the "Shared Kernel" concept in Domain-Driven Design</item>
/// <item>The Observability module is reserved for domain-specific observability features</item>
/// </list>
/// </para>
/// <para>
/// <b>Logging Integration:</b> Centralized logging via Elasticsearch provides:
/// <list type="bullet">
/// <item>Centralized log aggregation across all pods and services</item>
/// <item>Powerful search and filtering of log data</item>
/// <item>Visualization and monitoring via dashboards (Kibana/Grafana)</item>
/// <item>Alerting based on log patterns and error rates</item>
/// <item>Long-term log retention and archival</item>
/// </list>
/// </para>
/// <para>
/// <b>Usage Pattern:</b>
/// <code><![CDATA[
/// // In Program.cs ConfigureServices
/// services.AddLogging(Configuration);
///
/// // In Program.cs CreateHostBuilder (for Serilog)
/// .UseSerilog((context, services, loggerConfiguration) =>
/// {
///     loggerConfiguration
///         .ReadFrom.Configuration(context.Configuration)
///         .Enrich.FromLogContext()
///         .WriteTo.Console()
///         .WriteTo.Elasticsearch(...);
/// });
/// ]]></code>
/// </para>
/// <para>
/// <b>Configuration Section:</b> By default, this extension reads from the
/// <c>"Chatify:Logging"</c> configuration section. Ensure your appsettings.json
/// or environment variables provide the required configuration:
/// <code><![CDATA[
/// {
///   "Chatify": {
///     "Logging": {
///       "Uri": "http://localhost:9200",
///       "Username": "elastic",
///       "Password": "changeme",
///       "IndexPrefix": "logs-chatify-chatapi"
///     }
///   }
/// }
/// ]]></code>
/// </para>
/// </remarks>
public static class ServiceCollectionLoggingExtensions
{
    /// <summary>
    /// Configures Chatify logging options and registers the <see cref="ILogService"/>
    /// with the dependency injection container.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to add services to.
    /// Must not be null.
    /// </param>
    /// <param name="configuration">
    /// The application configuration containing logging settings.
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
    /// Thrown when logging configuration is invalid or missing required fields.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Registered Services:</b> This method registers:
    /// <list type="bullet">
    /// <item><see cref="ILogService"/> as <see cref="LogService"/> (scoped)</item>
    /// <item><see cref="LoggingOptionsEntity"/> as a configured options object (singleton)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Options Binding:</b> The method binds configuration from the
    /// <c>"Chatify:Logging"</c> section to <see cref="LoggingOptionsEntity"/> and
    /// validates all required fields before registration.
    /// </para>
    /// <para>
    /// <b>Validation:</b> The following validations are performed:
    /// <list type="bullet">
    /// <item><see cref="LoggingOptionsEntity.Uri"/> must not be empty</item>
    /// <item><see cref="LoggingOptionsEntity.Uri"/> must be a well-formed URI</item>
    /// <item><see cref="LoggingOptionsEntity.IndexPrefix"/> must not be empty</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Service Lifetimes:</b>
    /// <list type="bullet">
    /// <item>ILogService: Scoped - ensures correlation context is correctly propagated within a request scope</item>
    /// <item>Logging options: Singleton (configuration is read-only)</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddLogging(this IServiceCollection services, IConfiguration configuration)
    {
        GuardUtility.NotNull(services);
        GuardUtility.NotNull(configuration);

        var loggingSection = configuration.GetSection("Chatify:Logging");
        var loggingOptions = loggingSection.Get<LoggingOptionsEntity>()
            ?? new LoggingOptionsEntity();

        if (!loggingOptions.IsValid())
        {
            throw new ArgumentException(
                $"Invalid logging configuration. " +
                $"Please check the 'Chatify:Logging' configuration section. " +
                $"Required fields: Uri, IndexPrefix. " +
                $"Provided options: {loggingOptions}",
                nameof(configuration));
        }

        // Register logging options as a singleton for access by Serilog configuration
        services.AddSingleton(loggingOptions);

        // Register ILogService as scoped for proper correlation context propagation
        services.AddScoped<ILogService, LogService>();

        return services;
    }
}
