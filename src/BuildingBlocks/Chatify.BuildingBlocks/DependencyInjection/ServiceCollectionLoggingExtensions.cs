using Chatify.BuildingBlocks.Primitives;
using Microsoft.Extensions.DependencyInjection;

namespace Chatify.BuildingBlocks.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring logging integration
/// in the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This class contains extension methods that encapsulate
/// the registration of <see cref="ILogService"/> for application-level logging.
/// </para>
/// <para>
/// <b>Location:</b> This extension is placed in BuildingBlocks rather than the
/// Observability module because logging is a fundamental cross-cutting primitive.
/// </para>
/// <para>
/// <b>Usage Pattern:</b>
/// <code><![CDATA[
/// // In Program.cs ConfigureServices
/// services.AddLogging();
///
/// // In Program.cs CreateHostBuilder (for Serilog)
/// .UseSerilog((context, services, loggerConfiguration) =>
/// {
///     var loggingOptions = context.Configuration
///         .GetSection("Chatify:Logging")
///         .Get<LoggingOptionsEntity>();
///
///     loggerConfiguration
///         .ReadFrom.Configuration(context.Configuration)
///         .WriteTo.Console()
///         .WriteTo.Elasticsearch(...);
/// });
/// ]]></code>
/// </para>
/// </remarks>
public static class ServiceCollectionLoggingExtensions
{
    /// <summary>
    /// Registers the <see cref="ILogService"/> with the dependency injection container.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to add services to.
    /// Must not be null.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance so that multiple
    /// calls can be chained.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> is null.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Registered Services:</b> This method registers:
    /// <list type="bullet">
    /// <item><see cref="ILogService"/> as <see cref="LogService"/> (scoped)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Service Lifetime:</b> Scoped - ensures correlation context is correctly
    /// propagated within a request scope.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddLogging(this IServiceCollection services)
    {
        GuardUtility.NotNull(services);

        // Register ILogService as scoped for proper correlation context propagation
        services.AddScoped<ILogService, LogService>();

        return services;
    }
}
