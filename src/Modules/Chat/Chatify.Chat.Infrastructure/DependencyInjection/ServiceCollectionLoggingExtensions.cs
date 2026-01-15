using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chatify.Chat.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring logging integration
/// in the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This class contains extension methods that encapsulate
/// the configuration of centralized log aggregation via Serilog.
/// This approach keeps the Program.cs clean and provides a single, discoverable
/// location for logging configuration.
/// </para>
/// <para>
/// <b>Logging Integration:</b> Centralized logging is used as the log store for Chatify, providing:
/// <list type="bullet">
/// <item>Centralized log aggregation across all pods and services</item>
/// <item>Powerful search and filtering of log data</item>
/// <item>Visualization and monitoring via dashboards</item>
/// <item>Alerting based on log patterns and error rates</item>
/// <item>Long-term log retention and archival</item>
/// </list>
/// </para>
/// <para>
/// <b>Usage Pattern:</b>
/// <code><![CDATA[
/// // In Program.cs
/// builder.Services.AddLogging(
///     builder.Configuration
/// );
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
///       "IndexPrefix": "logs-chatify"
///     }
///   }
/// }
/// ]]></code>
/// </para>
/// <para>
/// <b>Note:</b> This extension only validates and stores the logging options.
/// The actual Serilog sink configuration is done separately in the host setup.
/// </para>
/// </remarks>
public static class ServiceCollectionLoggingExtensions
{
    /// <summary>
    /// Configures Chatify logging options and registers them
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
    /// <item><see cref="ElasticOptionsEntity"/> as a configured options object (singleton)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Note:</b> Unlike other provider extensions, this method does not register
    /// any application services. It only validates and stores the logging
    /// options for use by the Serilog sink configuration in the host setup.
    /// </para>
    /// <para>
    /// <b>Options Binding:</b> The method binds configuration from the
    /// <c>"Chatify:Logging"</c> section to <see cref="ElasticOptionsEntity"/> and
    /// validates all required fields before registration.
    /// </para>
    /// <para>
    /// <b>Validation:</b> The following validations are performed:
    /// <list type="bullet">
    /// <item><see cref="ElasticOptionsEntity.Uri"/> must not be empty</item>
    /// <item><see cref="ElasticOptionsEntity.Uri"/> must be a well-formed URI</item>
    /// <item><see cref="ElasticOptionsEntity.IndexPrefix"/> must not be empty</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Service Lifetimes:</b>
    /// <list type="bullet">
    /// <item>Logging options: Singleton (configuration is read-only)</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddLogging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        GuardUtility.NotNull(services);
        GuardUtility.NotNull(configuration);

        var loggingSection = configuration.GetSection("Chatify:Logging");
        var loggingOptions = loggingSection.Get<ElasticOptionsEntity>()
            ?? new ElasticOptionsEntity();

        if (!loggingOptions.IsValid())
        {
            throw new ArgumentException(
                $"Invalid logging configuration. " +
                $"Please check the 'Chatify:Logging' configuration section. " +
                $"Required fields: Uri, IndexPrefix. " +
                $"Provided options: {loggingOptions}",
                nameof(configuration));
        }

        services.AddSingleton(loggingOptions);

        return services;
    }
}
