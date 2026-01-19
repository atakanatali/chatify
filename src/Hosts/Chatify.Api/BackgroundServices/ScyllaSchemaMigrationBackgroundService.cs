using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Infrastructure.Migrations;
using Chatify.Chat.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chatify.Api.BackgroundServices;

/// <summary>
/// Background service that applies ScyllaDB schema migrations on application startup.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This background service ensures that ScyllaDB schema migrations
/// are applied during application startup before the application begins handling requests.
/// This guarantees that the database schema matches the application's expectations.
/// </para>
/// <para>
/// <b>Startup Behavior:</b> The service runs once during application startup:
/// <list type="number">
/// <item>Checks if <see cref="ScyllaSchemaMigrationOptionsEntity.ApplySchemaOnStartup"/> is enabled</item>
/// <item>If enabled, applies pending migrations via <see cref="IScyllaSchemaMigrationService"/></item>
/// <item>Logs all migration activity for operational visibility</item>
/// <item>Completes quickly to allow application startup to proceed</item>
/// </list>
/// </para>
/// <para>
/// <b>Conditional Execution:</b> The migration process only runs when configured
/// via the <c>Chatify:Scylla:ApplySchemaOnStartup</c> configuration key. This allows
/// operators to disable automatic migrations in environments where manual schema
/// management is preferred (e.g., production with strict change control).
/// </para>
/// <para>
/// <b>Error Handling:</b> Migration failures are logged with full details.
/// Depending on <see cref="ScyllaSchemaMigrationOptionsEntity.FailFastOnSchemaError"/>,
/// the service may either throw (stopping startup) or log and continue.
/// </para>
/// <para>
/// <b>Execution Order:</b> This service should be registered before other background
/// services that depend on database schema, ensuring migrations are applied before
/// those services start consuming messages or handling requests.
/// </para>
/// </remarks>
public sealed class ScyllaSchemaMigrationBackgroundService : BackgroundService
{
    private readonly ScyllaSchemaMigrationOptionsEntity _options;
    private readonly IScyllaSchemaMigrationService _migrationService;
    private readonly ILogger<ScyllaSchemaMigrationBackgroundService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScyllaSchemaMigrationBackgroundService"/> class.
    /// </summary>
    /// <param name="options">
    /// The schema migration configuration options.
    /// </param>
    /// <param name="migrationService">
    /// The service for applying schema migrations.
    /// </param>
    /// <param name="logger">
    /// The logger for recording migration activity.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any required parameter is null.
    /// </exception>
    /// <remarks>
    /// Dependencies are injected via the DI container. The background service
    /// is registered as a singleton and executes once during application startup.
    /// </remarks>
    public ScyllaSchemaMigrationBackgroundService(
        ScyllaSchemaMigrationOptionsEntity options,
        IScyllaSchemaMigrationService migrationService,
        ILogger<ScyllaSchemaMigrationBackgroundService> logger)
    {
        GuardUtility.NotNull(options);
        GuardUtility.NotNull(migrationService);
        GuardUtility.NotNull(logger);

        _options = options;
        _migrationService = migrationService;
        _logger = logger;
    }

    /// <summary>
    /// Executes the schema migration process as part of the application startup.
    /// </summary>
    /// <param name="stoppingToken">
    /// A token to monitor for cancellation requests during shutdown.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Execution Flow:</b>
    /// <list type="number">
    /// <item>Check if <see cref="ScyllaSchemaMigrationOptionsEntity.ApplySchemaOnStartup"/> is enabled</item>
    /// <item>If disabled, log and complete immediately</item>
    /// <item>If enabled, call <see cref="IScyllaSchemaMigrationService.ApplyMigrationsAsync"/></item>
    /// <item>Log completion status</item>
    /// <item>Complete to allow application startup to proceed</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>One-Time Execution:</b> This method executes once during startup and then
    /// completes. Unlike other background services that run continuously, this service
    /// performs a single migration operation and exits gracefully.
    /// </para>
    /// <para>
    /// <b>Startup Blocking:</b> The application startup process will wait for this
    /// service to complete before allowing the application to handle requests. This
    /// ensures schema consistency before any data operations occur.
    /// </para>
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ensure we yield to the caller immediately so startup isn't blocked
        await Task.Yield();

        if (!_options.ApplySchemaOnStartup)
        {
            _logger.LogInformation(
                "ScyllaSchemaMigrationBackgroundService: Schema migrations on startup are disabled. " +
                "Enable via Chatify:Scylla:ApplySchemaOnStartup configuration.");
            return;
        }

        _logger.LogInformation(
            "ScyllaSchemaMigrationBackgroundService: Starting schema migration process. " +
            "Keyspace: {Keyspace}, Migration table: {MigrationTable}, Fail fast: {FailFast}",
            _options.Keyspace,
            _options.SchemaMigrationTableName,
            _options.FailFastOnSchemaError);

        try
        {
            await _migrationService.ApplyMigrationsAsync(stoppingToken);

            _logger.LogInformation(
                "ScyllaSchemaMigrationBackgroundService: Schema migration process completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ScyllaSchemaMigrationBackgroundService: Schema migration process failed. " +
                "Check logs for details.");

            if (_options.FailFastOnSchemaError)
            {
                _logger.LogError(
                    "ScyllaSchemaMigrationBackgroundService: FailFastOnSchemaError is enabled. " +
                    "Application startup will be aborted.");
                throw;
            }

            _logger.LogWarning(
                "ScyllaSchemaMigrationBackgroundService: FailFastOnSchemaError is disabled. " +
                "Application startup will continue despite migration failures.");
        }
    }
}
