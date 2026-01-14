using Chatify.Chat.Application.Commands.SendChatMessage;
using Microsoft.Extensions.DependencyInjection;

namespace Chatify.Chat.Application.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring Chatify Chat Application layer
/// services in the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This class contains extension methods that encapsulate
/// the registration of Application layer services. This approach keeps
/// the Program.cs clean and provides a single, discoverable location for
/// application service configuration.
/// </para>
/// <para>
/// <b>Clean Architecture:</b> The Application layer registers its own
/// services (command handlers, application services) but does not register
/// infrastructure implementations. Infrastructure services are registered
/// by the Infrastructure layer's own DI extension methods.
/// </para>
/// <para>
/// <b>Lifetime Management:</b> Command handlers are registered as scoped
/// services because they may depend on scoped services like the
/// correlation context accessor. This ensures a new handler instance is
/// created for each HTTP request.
/// </para>
/// <para>
/// <b>Usage Pattern:</b>
/// <code><![CDATA[
/// // In Program.cs
/// builder.Services.AddChatifyChatApplication();
/// ]]></code>
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Chatify Chat Application layer services with the dependency
    /// injection container.
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
    /// <item>All command handlers (e.g., <see cref="SendChatMessageCommandHandler"/>)</item>
    /// <item>Application-level services (none in the current implementation)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Service Lifetimes:</b>
    /// <list type="bullet">
    /// <item>Command handlers: Scoped (created per request)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Open Generic Registration:</b> As the application grows with multiple
    /// command/query handlers, consider using open generic registration or
    /// marker interfaces to auto-register handlers:
    /// <code><![CDATA[
    /// var handlerAssembly = typeof(ServiceCollectionExtensions).Assembly;
    /// services.Scan(scan => scan
    ///     .FromAssemblies(handlerAssembly)
    ///     .AddClasses(classes => classes.AssignableTo(typeof(IHandler<>)))
    ///     .AsImplementedInterfaces()
    ///     .WithScopedLifetime());
    /// ]]></code>
    /// </para>
    /// <para>
    /// <b>Call Order:</b> This method should be called in Program.cs after
    /// registering infrastructure services but before building the host.
    /// Infrastructure services are registered separately via the Infrastructure
    /// layer's extension method.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddChatifyChatApplication(this IServiceCollection services)
    {
        GuardUtility.NotNull(services);

        // Register command handlers
        // Handlers are scoped because they may depend on scoped services
        // like ICorrelationContextAccessor or ILogger<T>
        services.AddScoped<SendChatMessageCommandHandler>();

        // Additional command handlers will be registered here as the
        // application grows. Consider using assembly scanning for
        // automatic registration when there are many handlers.

        return services;
    }
}
