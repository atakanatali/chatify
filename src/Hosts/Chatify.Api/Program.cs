using Chatify.Api.BackgroundServices;
using Chatify.Api.Hubs;
using Chatify.Api.Middleware;
using Chatify.BuildingBlocks.DependencyInjection;
using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Application.DependencyInjection;
using Chatify.Chat.Application.Ports;
using Chatify.Chat.Infrastructure.DependencyInjection;
using Chatify.Chat.Infrastructure.Services.PodIdentity;
using Chatify.Chat.Infrastructure.Services.RateLimit;
using Microsoft.Extensions.Logging;

namespace Chatify.Api;

/// <summary>
/// Test-mode implementation of <see cref="IRateLimitService"/> that always allows requests.
/// </summary>
/// <remarks>
/// This implementation is used in test mode when Redis is not available.
/// It always returns success for rate limit checks.
/// </remarks>
internal sealed class TestRateLimitService : IRateLimitService
{
    /// <inheritdoc/>
    public Task<ResultEntity> CheckAndIncrementAsync(
        string key,
        int threshold,
        int windowSeconds,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ResultEntity.Success());
    }
}

/// <summary>
/// Test-mode implementation of <see cref="IPodIdentityService"/> that returns a fixed pod ID.
/// </summary>
/// <remarks>
/// This implementation is used in test mode when external services are not available.
/// </remarks>
internal sealed class TestPodIdentityService : IPodIdentityService
{
    /// <inheritdoc/>
    public string PodId => "test-pod";
}

/// <summary>
/// The Program class for the Chatify API application.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This is the entry point for the Chatify API application.
/// It configures the ASP.NET Core host, dependency injection, middleware pipeline,
/// and starts the web server.
/// </para>
/// <para>
/// <b>Architecture:</b> The application follows Clean Architecture principles with:
/// <list type="bullet">
/// <item>BuildingBlocks for cross-cutting concerns (clock, correlation, logging)</item>
/// <item>Infrastructure providers (database, caching, message broker)</item>
/// <item>Application services (command handlers, use cases)</item>
/// <item>ASP.NET Core services (controllers, SignalR hubs)</item>
/// </list>
/// </para>
/// <para>
/// <b>Test Mode:</b> When the configuration contains <c>Chatify:TestMode=true</c>,
/// the application skips registration of external infrastructure services (database,
/// caching, background services) and registers test double implementations instead
/// to enable testing without external dependencies.
/// </para>
/// </remarks>
public class Program
{
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the application.</param>
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var configuration = builder.Configuration;
        var isTestMode = configuration.GetValue<bool>("Chatify:TestMode", false);

        // BuildingBlocks
        builder.Services.AddSingleton<IClockService, SystemClockService>();
        builder.Services.AddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();

        // Logging
        builder.Services.AddSerilog(configuration);

        if (!isTestMode)
        {
            // Infrastructure Providers - skip in test mode
            builder.Services.AddDatabase(configuration);
            builder.Services.AddCaching(configuration);
            builder.Services.AddChatHistoryWriter(configuration);

            // Background Services - skip in test mode
            builder.Services.AddHostedService<ChatBroadcastBackgroundService>();
            builder.Services.AddHostedService<ChatHistoryWriterBackgroundService>();

            // Pod identity service from infrastructure
            builder.Services.AddSingleton<IPodIdentityService, PodIdentityService>();
        }
        else
        {
            // Test mode: Register test doubles for external dependencies
            builder.Services.AddSingleton<IRateLimitService, TestRateLimitService>();
            builder.Services.AddSingleton<IPodIdentityService, TestPodIdentityService>();
        }

        // Message Broker - configured with in-memory option for tests
        builder.Services.AddMessageBroker(configuration);

        // Application Services
        builder.Services.AddChatifyChatApplication();

        // ASP.NET Core Services
        builder.Services.AddControllers();
        builder.Services.AddSignalR();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseRouting();

        // Eğer authentication kullanıyorsan aç:
        // app.UseAuthentication();

        app.UseAuthorization();

        app.MapControllers();
        app.MapHub<ChatHubService>("/hubs/chat");

        app.Run();
    }
}
