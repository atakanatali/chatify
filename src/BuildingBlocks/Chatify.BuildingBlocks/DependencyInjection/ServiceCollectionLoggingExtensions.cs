using Chatify.BuildingBlocks.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Sinks.Elasticsearch;

namespace Chatify.BuildingBlocks.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring logging integration
/// in the dependency injection container.
/// </summary>
public static class ServiceCollectionLoggingExtensions
{
    /// <summary>
    /// Registers the <see cref="ILogService"/> and configures Serilog with Elasticsearch sink.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddChatifyLogging(this IServiceCollection services, IConfiguration configuration)
    {
        // Register ILogService
        services.AddScoped<ILogService, LogService>();

        // Register logging options for Serilog configuration
        var loggingOptions = configuration.GetSection("Chatify:Logging").Get<LoggingOptionsEntity>()
            ?? new LoggingOptionsEntity();

        services.AddSingleton(loggingOptions);

        return services;
    }

    /// <summary>
    /// Configures Serilog with Elasticsearch sink for Chatify.
    /// </summary>
    /// <param name="loggerConfiguration">The Serilog logger configuration.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The configured logger configuration.</returns>
    public static LoggerConfiguration ConfigureChatifySerilog(
        this LoggerConfiguration loggerConfiguration,
        IConfiguration configuration)
    {
        var loggingOptions = configuration.GetSection("Chatify:Logging").Get<LoggingOptionsEntity>()
            ?? new LoggingOptionsEntity();

        loggerConfiguration
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", "Chatify.ChatApi")
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");

        if (loggingOptions.IsValid())
        {
            loggerConfiguration.WriteTo.Elasticsearch(new[] { new Uri(loggingOptions.Uri) }, options =>
            {
                options.DataStream = new Elastic.Ingest.Elasticsearch.DataStreams.DataStreamName(loggingOptions.IndexPrefix, "date");
                options.BootstrapMethod = Elastic.Ingest.Elasticsearch.ElasticsearchIngestBootstrapMethod.Failure;
                options.ConfigureChannel = channelOptions =>
                {
                    channelOptions.BufferOptions = new Elastic.Ingest.Elasticsearch.ElasticsearchBufferOptions
                    {
                        ExportMaxConcurrency = 1
                    };
                };

                if (!string.IsNullOrWhiteSpace(loggingOptions.Username) && !string.IsNullOrWhiteSpace(loggingOptions.Password))
                {
                    options.Authentication = new Elastic.Clients.Elasticsearch.Core.BasicAuthentication(
                        loggingOptions.Username,
                        loggingOptions.Password);
                }
            });
        }

        return loggerConfiguration;
    }
}
