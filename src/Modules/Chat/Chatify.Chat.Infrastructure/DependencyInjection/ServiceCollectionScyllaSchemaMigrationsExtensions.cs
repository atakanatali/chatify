using System.Reflection;
using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Infrastructure.Migrations;
using Chatify.Chat.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Chatify.Chat.Infrastructure.DependencyInjection;

/// <summary>
/// Provides extension methods for configuring ScyllaDB schema migration services
/// in the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This class contains extension methods that encapsulate the registration
/// of ScyllaDB schema migration services. This approach keeps the Program.cs clean and
/// provides a single, discoverable location for schema migration configuration.
/// </para>
/// <para>
/// <b>Code-First Migrations:</b> Chatify uses a code-first migration approach where each
/// migration is a C# class implementing <see cref="IScyllaSchemaMigration"/>. Migrations
/// are discovered by scanning assemblies and tracked in a special table to ensure each
/// migration is only applied once.
/// </para>
/// <para>
/// <b>Usage Pattern:</b>
/// <code><![CDATA[
/// // In Program.cs
/// builder.Services.AddScyllaSchemaMigrationsChatify(
///     builder.Configuration
/// );
/// ]]></code>
/// </para>
/// <para>
/// <b>Configuration Section:</b> This extension reads from the <c>"Chatify:Scylla"</c>
/// configuration section. Example appsettings.json:
/// <code><![CDATA[
/// {
///   "Chatify": {
///     "Scylla": {
///       "Keyspace": "chatify",
///       "ApplySchemaOnStartup": true,
///       "SchemaMigrationTableName": "schema_migrations",
///       "FailFastOnSchemaError": true
///     }
///   }
/// }
/// ]]></code>
/// </para>
/// <para>
/// <b>Migration Discovery:</b> Migrations are automatically discovered from:
/// <list type="bullet">
/// <item>The calling assembly (typically the Infrastructure project)</item>
/// <item>Any additional assemblies specified via options</item>
/// <item>All types implementing <see cref="IScyllaSchemaMigration"/></item>
/// </list>
/// </para>
/// <para>
/// <b>Startup Behavior:</b> When <see cref="ScyllaSchemaMigrationOptionsEntity.ApplySchemaOnStartup"/>
/// is enabled, migrations are applied during application startup. This is controlled by
/// configuration rather than code, making it easy to disable in specific environments.
/// </para>
/// </remarks>
public static class ServiceCollectionScyllaSchemaMigrationsExtensions
{
    /// <summary>
    /// Registers Chatify ScyllaDB schema migration services with the dependency
    /// injection container.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to add services to.
    /// Must not be null.
    /// </param>
    /// <param name="configuration">
    /// The application configuration containing ScyllaDB settings.
    /// Must not be null.
    /// </param>
    /// <param name="assembliesToScan">
    /// Optional list of assemblies to scan for migrations. If not provided, the calling
    /// assembly is scanned by default.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance so that multiple calls can be chained.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when schema migration configuration is invalid or missing required fields.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Registered Services:</b> This method registers:
    /// <list type="bullet">
    /// <item><see cref="ScyllaSchemaMigrationOptionsEntity"/> as a configured options object (singleton)</item>
    /// <item><see cref="ISchemaMigrationHistoryRepository"/> for migration history tracking (singleton)</item>
    /// <item><see cref="SchemaMigrationHistoryRepository"/> as the default implementation (singleton)</item>
    /// <item><see cref="IScyllaSchemaMigrationService"/> for applying migrations (singleton)</item>
    /// <item><see cref="ScyllaSchemaMigrationService"/> as the default implementation (singleton)</item>
    /// <item>All types implementing <see cref="IScyllaSchemaMigration"/> (transient)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Migration Discovery:</b> Migrations are discovered by scanning the provided
    /// assemblies (or the calling assembly by default) for types implementing
    /// <see cref="IScyllaSchemaMigration"/>. Each migration is registered as a
    /// transient service, allowing the migration service to receive all migrations
    /// via constructor injection.
    /// </para>
    /// <para>
    /// <b>Configuration Binding:</b> The options are bound from the <c>"Chatify:Scylla"</c>
    /// configuration section. The same section is used for both connection options and
    /// migration options, keeping the configuration structure simple.
    /// </para>
    /// <para>
    /// <b>Order of Registration:</b> This extension should be called AFTER
    /// <see cref="ServiceCollectionScyllaExtensions.AddScyllaChatify"/> to ensure
    /// the Cassandra session is available for migrations.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddScyllaSchemaMigrationsChatify(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] assembliesToScan)
    {
        GuardUtility.NotNull(services);
        GuardUtility.NotNull(configuration);

        // If no assemblies specified, use the calling assembly
        if (assembliesToScan.Length == 0)
        {
            assembliesToScan = [Assembly.GetCallingAssembly()];
        }

        // Bind configuration from the "Chatify:Scylla" section
        var scyllaSection = configuration.GetSection("Chatify:Scylla");
        var migrationOptions = new ScyllaSchemaMigrationOptionsEntity
        {
            Keyspace = scyllaSection.GetValue<string>("Keyspace") ?? "chatify",
            ApplySchemaOnStartup = scyllaSection.GetValue<bool>("ApplySchemaOnStartup", true),
            SchemaMigrationTableName = scyllaSection.GetValue<string>("SchemaMigrationTableName") ?? "schema_migrations",
            FailFastOnSchemaError = scyllaSection.GetValue<bool>("FailFastOnSchemaError", true)
        };

        // Validate the configuration
        if (!migrationOptions.IsValid())
        {
            throw new ArgumentException(
                $"Invalid ScyllaDB schema migration configuration. " +
                $"Please check the 'Chatify:Scylla' configuration section. " +
                $"Provided options: {migrationOptions}",
                nameof(configuration));
        }

        // Register the options as a singleton
        services.AddSingleton(migrationOptions);

        // Register the migration history repository
        services.AddSingleton<ISchemaMigrationHistoryRepository, SchemaMigrationHistoryRepository>();

        // Discover and register all migrations
        var migrationTypes = assembliesToScan
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsInterface && typeof(IScyllaSchemaMigration).IsAssignableFrom(t))
            .ToList();

        foreach (var migrationType in migrationTypes)
        {
            services.AddTransient(typeof(IScyllaSchemaMigration), migrationType);
        }

        // Register the migration service (depends on all migrations being registered)
        services.AddSingleton<IScyllaSchemaMigrationService, ScyllaSchemaMigrationService>();

        return services;
    }
}
