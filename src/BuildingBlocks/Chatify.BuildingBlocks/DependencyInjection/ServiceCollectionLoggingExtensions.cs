using Chatify.BuildingBlocks.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace Chatify.BuildingBlocks.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring logging integration
/// in the dependency injection container.
/// </summary>
public static class ServiceCollectionLoggingExtensions
{
    /// <summary>
    /// Configures Serilog with Elasticsearch sink and registers ILogService.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddSerilog(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var loggingOptions = configuration.GetSection("Chatify:Logging").Get<LoggingOptionsEntity>()
            ?? new LoggingOptionsEntity();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", "Chatify.ChatApi")
            .CreateLogger();

        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton(loggingOptions);

        return services;
    }
}
