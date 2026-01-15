using Chatify.Chat.Application.DependencyInjection;
using Chatify.Chat.Infrastructure.DependencyInjection;
using Serilog;

namespace Chatify.ChatApi;

/// <summary>
/// The entry point for the Chatify Chat API application.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This class contains the application entry point and
/// initializes the ASP.NET Core host with all required services, middleware,
/// and infrastructure providers.
/// </para>
/// <para>
/// <b>Architecture:</b> Chatify follows Clean Architecture with a modular
/// monolith design. The Program.cs orchestrates the dependency injection
/// container by calling extension methods from the Application and Infrastructure
/// layers.
/// </para>
/// <para>
/// <b>Provider Registration:</b> Infrastructure providers are registered via
/// their respective DI extension methods in the following order:
/// <list type="bullet">
/// <item>Elasticsearch logging (must be registered first for Serilog configuration)</item>
/// <item>ScyllaDB (persistent storage)</item>
/// <item>Redis (caching, presence, rate limiting)</item>
/// <item>Kafka (message streaming)</item>
/// <item>Application layer services (command handlers, application services)</item>
/// </list>
/// </para>
/// <para>
/// <b>Configuration:</b> The application loads configuration from multiple
/// sources in the following order (later sources override earlier ones):
/// <list type="bullet">
/// <item>appsettings.json (base configuration)</item>
/// <item>appsettings.{Environment}.json (environment-specific)</item>
/// <item>Environment variables</item>
/// <item>Command line arguments</item>
/// </list>
/// </para>
/// <para>
/// <b>Logging:</b> Serilog is configured as the logging provider with the
/// Elasticsearch sink. Logs are written to the console for immediate feedback
/// and to Elasticsearch for long-term storage and analysis.
/// </para>
/// </remarks>
public static class Program
{
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method builds the web host and runs the application. It uses
    /// the <see cref="Host"/> builder pattern to configure services,
    /// middleware, and the application pipeline.
    /// </para>
    /// </remarks>
    public static void Main(string[] args)
    {
        // Build the host and run the application
        // The host builder pattern allows for fine-grained control over
        // dependency injection, configuration, logging, and middleware
        var host = CreateHostBuilder(args).Build();

        // Run the application (blocks until shutdown)
        host.Run();
    }

    /// <summary>
    /// Creates and configures the web host builder.
    /// </summary>
    /// <param name="args">
    /// Command line arguments passed to the application.
    /// </param>
    /// <returns>
    /// A configured <see cref="IHost"/> instance ready to run.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Configuration:</b> This method configures the host with:
    /// <list type="bullet">
    /// <item>Default configuration providers (appsettings, environment variables)</item>
    /// <item>Serilog as the logging provider</item>
    /// <item>Kestrel as the web server</item>
    /// <item>All required infrastructure and application services</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Service Registration Order:</b>
    /// <list type="number">
    /// <item>Elasticsearch options (for Serilog configuration)</item>
    /// <item>Infrastructure providers (ScyllaDB, Redis, Kafka)</item>
    /// <item>Application services (command handlers)</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            // Configure Serilog to use the Elasticsearch sink
            // This must be done early to capture all startup logs
            .UseSerilog((context, services, loggerConfiguration) =>
            {
                loggerConfiguration
                    .ReadFrom.Configuration(context.Configuration)
                    .Enrich.FromLogContext()
                    .Enrich.WithMachineName()
                    .Enrich.WithEnvironmentName()
                    .Enrich.WithProcessId()
                    .Enrich.WithThreadId()
                    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                    // Elasticsearch sink will be configured in a future step
                    // .WriteTo.Elasticsearch(...)
                    ;
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            });

    /// <summary>
    /// Configures the application services and middleware pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Startup Class:</b> This class is automatically discovered and instantiated
    /// by the ASP.NET Core hosting layer. It contains two key methods:
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/> - Registers services in the DI container</item>
    /// <item><see cref="Configure"/> - Configures the HTTP request pipeline</item>
    /// </list>
    /// </para>
    /// <para>
    /// The Startup class separates service registration from middleware configuration,
    /// making the code more organized and testable.
    /// </para>
    /// </remarks>
    public class Startup
    {
        /// <summary>
        /// Gets the application configuration.
        /// </summary>
        /// <remarks>
        /// This configuration is populated from appsettings.json, environment
        /// variables, and other configuration providers. It is used throughout
        /// the application to access strongly-typed settings.
        /// </remarks>
        public IConfiguration Configuration { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Startup"/> class.
        /// </summary>
        /// <param name="configuration">
        /// The application configuration. Automatically provided by the host.
        /// </param>
        /// <remarks>
        /// The constructor is called by the hosting layer and receives the
        /// fully-populated configuration object.
        /// </remarks>
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// Configures the application's dependency injection container.
        /// </summary>
        /// <param name="services">
        /// The service collection to register services with.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>Service Registration:</b> This method registers all application
        /// and infrastructure services in the DI container. The order of
        /// registration matters:
        /// </para>
        /// <para>
        /// <b>1. Infrastructure Options:</b>
        /// <list type="bullet">
        /// <item>Elasticsearch - Must be registered first for Serilog</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>2. Infrastructure Providers:</b>
        /// <list type="bullet">
        /// <item>ScyllaDB - Chat history persistence</item>
        /// <item>Redis - Presence, rate limiting, caching</item>
        /// <item>Kafka - Event streaming and messaging</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>3. Application Services:</b>
        /// <list type="bullet">
        /// <item>Command handlers - Application use case orchestration</item>
        /// <item>Other application services - (added as needed)</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>Configuration:</b> All extension methods read from the
        /// <see cref="Configuration"/> object, which loads settings from:
        /// <list type="bullet">
        /// <item>appsettings.json</item>
        /// <item>appsettings.{Environment}.json</item>
        /// <item>Environment variables</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>Required Configuration Sections:</b>
        /// <list type="bullet">
        /// <item>Chatify:Elastic - Elasticsearch connection settings</item>
        /// <item>Chatify:Scylla - ScyllaDB connection settings</item>
        /// <item>Chatify:Redis - Redis connection settings</item>
        /// <item>Chatify:Kafka - Kafka connection settings</item>
        /// </list>
        /// </para>
        /// </remarks>
        public void ConfigureServices(IServiceCollection services)
        {
            // ============================================
            // STEP 1: Register Infrastructure Options
            // ============================================
            // Elasticsearch options must be registered first so Serilog can
            // use them for log shipping configuration
            services.AddElasticsearchLogging(Configuration);

            // ============================================
            // STEP 2: Register Infrastructure Providers
            // ============================================
            // Each provider extension reads its configuration and registers
            // the necessary services for that infrastructure component

            // Distributed Database: Chat message history persistence
            // Registers: IChatHistoryRepository
            services.AddDistributedDatabase(Configuration);

            // Distributed Cache: Presence tracking, rate limiting, caching
            // Registers: IPresenceService, IRateLimitService
            services.AddDistributedCache(Configuration);

            // Message Broker: Event streaming and async messaging
            // Registers: IChatEventProducerService
            services.AddKafka(Configuration);

            // ============================================
            // STEP 3: Register Application Services
            // ============================================
            // Application layer services depend on infrastructure services
            // and must be registered after infrastructure

            // Chat application: Command handlers and application services
            services.AddChatifyChatApplication();

            // ============================================
            // STEP 4: Register ASP.NET Core Services
            // ============================================
            // Add controllers, SignalR, etc. (will be added in future steps)
            services.AddControllers();
            services.AddSignalR();

            // Add health checks for infrastructure providers
            // (will be added in future steps)
            // services.AddHealthChecks()
            //     .AddScyllaHealthCheck(...)
            //     .AddRedisHealthCheck(...)
            //     .AddKafkaHealthCheck(...);
        }

        /// <summary>
        /// Configures the HTTP request pipeline.
        /// </summary>
        /// <param name="app">
        /// The application builder for configuring the middleware pipeline.
        /// </param>
        /// <param name="env">
        /// Information about the hosting environment.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>Middleware Pipeline:</b> This method configures the middleware
        /// pipeline that processes each HTTP request. Middleware is executed
        /// in the order it is registered here.
        /// </para>
        /// <para>
        /// <b>Current Middleware:</b>
        /// <list type="bullet">
        /// <item>Developer exception page (development only)</item>
        /// <item>Routing</item>
        /// <item>Authorization</item>
        /// <item>Controllers</item>
        /// <item>SignalR hubs (will be added in future steps)</item>
        /// </list>
        /// </para>
        /// <para>
        /// Additional middleware will be added as the application grows:
        /// <list type="bullet">
        /// <item>Correlation ID middleware</item>
        /// <item>Request logging middleware</item>
        /// <item>Rate limiting middleware</item>
        /// <item>Authentication/authorization</item>
        /// </list>
        /// </para>
        /// </remarks>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                // Developer exception page shows detailed error information
                // Only enable in development to avoid leaking sensitive information
                app.UseDeveloperExceptionPage();
            }

            // Enable routing for controllers and SignalR hubs
            app.UseRouting();

            // Authorization middleware must come after routing but before endpoints
            app.UseAuthorization();

            // Configure the HTTP endpoints (controllers, SignalR hubs, etc.)
            app.UseEndpoints(endpoints =>
            {
                // Map controller endpoints
                endpoints.MapControllers();

                // Map SignalR hubs (will be added in future steps)
                // endpoints.MapHub<ChatHub>("/hubs/chat");

                // Map health check endpoint (will be added in future steps)
                // endpoints.MapHealthChecks("/health");
            });
        }
    }
}
