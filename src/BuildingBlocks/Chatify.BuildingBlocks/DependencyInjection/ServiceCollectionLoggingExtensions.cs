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

        var loggerConfiguration = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration) // Allow appsettings to override
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
            .WriteTo.Console(); // Always write to console for Filebeat/Debugging

        // Configure Elasticsearch sink if URI is valid
        if (loggingOptions.IsValid())
        {
            var indexFormat = $"{loggingOptions.IndexPrefix}-{DateTime.UtcNow:yyyy.MM.dd}";
            
            // Note: In a real scenario, we might want to use a more robust sink configuration
            // or use the specific Elastic.Serilog.Sinks package.
            // For now, we assume Serilog.Sinks.Elasticsearch is available.
            // If strictly using Filebeat, this might be redundant, but requested by user for robustness.
            loggerConfiguration.WriteTo.Elasticsearch(new Serilog.Sinks.Elasticsearch.ElasticsearchSinkOptions(new Uri(loggingOptions.Uri))
            {
                AutoRegisterTemplate = true,
                AutoRegisterTemplateVersion = Serilog.Sinks.Elasticsearch.AutoRegisterTemplateVersion.ESv7, // Compatible with 8.x usually
                IndexFormat = $"{loggingOptions.IndexPrefix}-{{0:yyyy.MM.dd}}",
                ModifyConnectionSettings = c => 
                {
                   if (!string.IsNullOrWhiteSpace(loggingOptions.Username) && !string.IsNullOrWhiteSpace(loggingOptions.Password))
                   {
                       c.BasicAuthentication(loggingOptions.Username, loggingOptions.Password);
                   }
                   return c;
                }
            });
        }

        Log.Logger = loggerConfiguration.CreateLogger();

        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton(loggingOptions);

        return services;
    }
}
