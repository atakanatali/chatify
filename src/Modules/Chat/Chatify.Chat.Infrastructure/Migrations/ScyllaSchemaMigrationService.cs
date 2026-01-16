using System.Reflection;
using Cassandra;
using Chatify.BuildingBlocks.DependencyInjection;
using Chatify.BuildingBlocks.Primitives;
using Chatify.Chat.Infrastructure.Options;
using Microsoft.Extensions.Logging;

namespace Chatify.Chat.Infrastructure.Migrations;

/// <summary>
/// Service for managing and applying ScyllaDB schema migrations in Chatify.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This service is responsible for discovering, tracking, and applying
/// schema migrations to ensure the database schema evolves consistently across deployments.
/// It implements a migration system similar to EF Core's migrations but tailored for
/// ScyllaDB and Chatify's code-first approach.
/// </para>
/// <para>
/// <b>Migration Discovery:</b> The service discovers migrations by scanning assemblies
/// for types implementing <see cref="IScyllaSchemaMigration"/>. This allows migrations
/// to be defined in any module's Infrastructure project and automatically discovered.
/// </para>
/// <para>
/// <b>Migration Tracking:</b> Applied migrations are tracked in a special table
/// (similar to <c>__EFMigrationsHistory</c>) to ensure each migration is only applied
/// once. The tracking table is created automatically if it doesn't exist.
/// </para>
/// <para>
/// <b>Startup Behavior:</b> When <see cref="ScyllaSchemaMigrationOptionsEntity.ApplySchemaOnStartup"/>
/// is enabled, migrations are applied during application startup before the application
/// begins handling requests.
/// </para>
/// <para>
/// <b>Error Handling:</b> The service supports both fail-fast and continue-on-error
/// modes via <see cref="ScyllaSchemaMigrationOptionsEntity.FailFastOnSchemaError"/>.
/// All errors are logged with full context for troubleshooting.
/// </para>
/// </remarks>
public sealed class ScyllaSchemaMigrationService : IScyllaSchemaMigrationService
{
    private readonly ISession _session;
    private readonly ScyllaSchemaMigrationOptionsEntity _options;
    private readonly ILogger<ScyllaSchemaMigrationService> _logger;
    private readonly ISchemaMigrationHistoryRepository _historyRepository;
    private readonly IEnumerable<IScyllaSchemaMigration> _migrations;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScyllaSchemaMigrationService"/> class.
    /// </summary>
    /// <param name="session">
    /// The Cassandra/ScyllaDB session connected to the keyspace.
    /// </param>
    /// <param name="options">
    /// The schema migration configuration options.
    /// </param>
    /// <param name="logger">
    /// The logger for recording migration operations and errors.
    /// </param>
    /// <param name="historyRepository">
    /// The repository for managing migration history.
    /// </param>
    /// <param name="migrations">
    /// The collection of discovered schema migrations.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when any required parameter is null.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Dependency Injection:</b> This constructor is called by the DI container.
    /// All migrations implementing <see cref="IScyllaSchemaMigration"/> are automatically
    /// injected via the <paramref name="migrations"/> parameter.
    /// </para>
    /// <para>
    /// <b>Migration Discovery:</b> Migrations are discovered by scanning assemblies
    /// for types implementing <see cref="IScyllaSchemaMigration"/>. Each migration is
    /// registered as a transient service in the DI container, allowing the service to
    /// receive all migrations via constructor injection.
    /// </para>
    /// </remarks>
    public ScyllaSchemaMigrationService(
        ISession session,
        ScyllaSchemaMigrationOptionsEntity options,
        ILogger<ScyllaSchemaMigrationService> logger,
        ISchemaMigrationHistoryRepository historyRepository,
        IEnumerable<IScyllaSchemaMigration> migrations)
    {
        GuardUtility.NotNull(session);
        GuardUtility.NotNull(options);
        GuardUtility.NotNull(logger);
        GuardUtility.NotNull(historyRepository);
        GuardUtility.NotNull(migrations);

        _session = session;
        _options = options;
        _logger = logger;
        _historyRepository = historyRepository;
        _migrations = migrations;
    }

    /// <summary>
    /// Applies all pending schema migrations to the database.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a migration fails and <see cref="ScyllaSchemaMigrationOptionsEntity.FailFastOnSchemaError"/>
    /// is <c>true</c>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Execution Flow:</b>
    /// <list type="number">
    /// <item>Ensure migration history table exists</item>
    /// <item>Query for already-applied migrations</item>
    /// <item>Filter migrations to find pending ones</item>
    /// <item>Sort migrations by name (version order)</item>
    /// <item>Apply each pending migration</item>
    /// <item>Record each applied migration in history</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Logging:</b> The service logs at each step:
    /// <list type="bullet">
    /// <item>Information: Starting migration process, discovered migrations, applied count</item>
    /// <item>Warning: Skipped migrations (already applied)</item>
    /// <item>Error: Migration failures with full CQL and exception details</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Ordering:</b> Migrations are sorted alphabetically by name to ensure
    /// version-based ordering (e.g., V001, V002, V003). Use naming conventions
    /// that ensure correct order (zero-padding is recommended).
    /// </para>
    /// <para>
    /// <b>Error Handling:</b>
    /// <list type="bullet">
    /// <item>If <c>FailFastOnSchemaError = true</c>: First error throws exception</item>
    /// <item>If <c>FailFastOnSchemaError = false</c>: Errors logged, remaining migrations continue</item>
    /// </list>
    /// </para>
    /// </remarks>
    public async Task ApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ScyllaSchemaMigrationService: Starting migration process. Keyspace: {Keyspace}, Migration table: {MigrationTable}",
            _options.Keyspace,
            _options.SchemaMigrationTableName);

        // Step 1: Ensure migration history table exists
        await _historyRepository.EnsureTableExistsAsync(cancellationToken);
        _logger.LogInformation("ScyllaSchemaMigrationService: Migration history table verified/created");

        // Step 2: Get applied migrations
        var appliedMigrationNames = await _historyRepository.GetAppliedMigrationNamesAsync(cancellationToken);
        var appliedSet = new HashSet<string>(appliedMigrationNames, StringComparer.Ordinal);
        _logger.LogInformation(
            "ScyllaSchemaMigrationService: Found {AppliedCount} previously applied migrations",
            appliedSet.Count);

        // Step 3: Filter and sort pending migrations
        var pendingMigrations = _migrations
            .Where(m => !appliedSet.Contains(m.Name))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        if (pendingMigrations.Count == 0)
        {
            _logger.LogInformation("ScyllaSchemaMigrationService: No pending migrations to apply");
            return;
        }

        _logger.LogInformation(
            "ScyllaSchemaMigrationService: Discovered {PendingCount} pending migrations to apply",
            pendingMigrations.Count);

        // Step 4: Apply each pending migration
        var appliedCount = 0;
        var failedCount = 0;

        foreach (var migration in pendingMigrations)
        {
            try
            {
                _logger.LogInformation(
                    "ScyllaSchemaMigrationService: Applying migration {MigrationName} from {AppliedBy}",
                    migration.Name,
                    migration.AppliedBy);

                // Apply the migration
                await migration.ApplyAsync(_session, cancellationToken);

                // Record in history
                await _historyRepository.RecordMigrationAsync(
                    migration.Name,
                    migration.AppliedBy,
                    DateTime.UtcNow,
                    cancellationToken);

                appliedCount++;
                _logger.LogInformation(
                    "ScyllaSchemaMigrationService: Successfully applied migration {MigrationName}",
                    migration.Name);
            }
            catch (Exception ex)
            {
                failedCount++;
                _logger.LogError(ex,
                    "ScyllaSchemaMigrationService: Failed to apply migration {MigrationName}. Error: {ErrorMessage}",
                    migration.Name,
                    ex.Message);

                if (_options.FailFastOnSchemaError)
                {
                    throw new InvalidOperationException(
                        $"Schema migration failed: {migration.Name}. Enable logging for details. " +
                        $"Applied: {appliedCount}, Failed: {failedCount}, Remaining: {pendingMigrations.Count - appliedCount - failedCount}",
                        ex);
                }
            }
        }

        _logger.LogInformation(
            "ScyllaSchemaMigrationService: Migration process completed. Applied: {AppliedCount}, Failed: {FailedCount}, Skipped: {SkippedCount}",
            appliedCount,
            failedCount,
            pendingMigrations.Count - appliedCount - failedCount);
    }
}

/// <summary>
/// Defines a contract for managing migration history in ScyllaDB.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This interface provides operations for tracking which migrations
/// have been applied to the database. The history is stored in a dedicated table
/// (similar to <c>__EFMigrationsHistory</c> in EF Core).
/// </para>
/// <para>
/// <b>Isolation:</b> This interface isolates the history tracking logic from the
/// main migration service, making it easier to test and maintain.
/// </para>
/// </remarks>
public interface ISchemaMigrationHistoryRepository
{
    /// <summary>
    /// Ensures the migration history table exists in the keyspace.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Idempotency:</b> This method uses <c>CREATE TABLE IF NOT EXISTS</c>,
    /// making it safe to call multiple times.
    /// </para>
    /// </remarks>
    Task EnsureTableExistsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the names of all migrations that have been applied.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation. The result contains
    /// the collection of applied migration names.
    /// </returns>
    Task<IReadOnlyList<string>> GetAppliedMigrationNamesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Records a migration as applied in the history table.
    /// </summary>
    /// <param name="migrationName">
    /// The unique name of the migration.
    /// </param>
    /// <param name="appliedBy">
    /// The module/assembly that applied the migration.
    /// </param>
    /// <param name="appliedAtUtc">
    /// The timestamp when the migration was applied (UTC).
    /// </param>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    Task RecordMigrationAsync(
        string migrationName,
        string appliedBy,
        DateTime appliedAtUtc,
        CancellationToken cancellationToken);
}

/// <summary>
/// Default implementation of <see cref="ISchemaMigrationHistoryRepository"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This class provides the default implementation for managing
/// migration history in ScyllaDB. It uses the Cassandra driver to execute CQL
/// statements against the migration history table.
/// </para>
/// </remarks>
public sealed class SchemaMigrationHistoryRepository : ISchemaMigrationHistoryRepository
{
    private readonly ISession _session;
    private readonly ScyllaSchemaMigrationOptionsEntity _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaMigrationHistoryRepository"/> class.
    /// </summary>
    /// <param name="session">
    /// The Cassandra/ScyllaDB session connected to the keyspace.
    /// </param>
    /// <param name="options">
    /// The schema migration configuration options.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="session"/> or <paramref name="options"/> is null.
    /// </exception>
    public SchemaMigrationHistoryRepository(
        ISession session,
        ScyllaSchemaMigrationOptionsEntity options)
    {
        GuardUtility.NotNull(session);
        GuardUtility.NotNull(options);

        _session = session;
        _options = options;
    }

    /// <inheritdoc/>
    public Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        var cql = $@"
            CREATE TABLE IF NOT EXISTS {_options.SchemaMigrationTableName} (
                migration_name text PRIMARY KEY,
                applied_by text,
                applied_at_utc timestamp
            );";

        var statement = new SimpleStatement(cql);
        return _session.ExecuteAsync(statement);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetAppliedMigrationNamesAsync(CancellationToken cancellationToken)
    {
        var cql = $"SELECT migration_name FROM {_options.SchemaMigrationTableName};";
        var statement = new SimpleStatement(cql);

        var rowSet = await _session.ExecuteAsync(statement);
        var migrationNames = new List<string>();

        foreach (var row in rowSet)
        {
            if (row != null)
            {
                var migrationName = row.GetValue<string>("migration_name");
                if (!string.IsNullOrWhiteSpace(migrationName))
                {
                    migrationNames.Add(migrationName);
                }
            }
        }

        return migrationNames;
    }

    /// <inheritdoc/>
    public Task RecordMigrationAsync(
        string migrationName,
        string appliedBy,
        DateTime appliedAtUtc,
        CancellationToken cancellationToken)
    {
        var cql = $@"
            INSERT INTO {_options.SchemaMigrationTableName} (
                migration_name, applied_by, applied_at_utc
            ) VALUES (?, ?, ?);";

        var statement = new SimpleStatement(cql, migrationName, appliedBy, appliedAtUtc);
        return _session.ExecuteAsync(statement);
    }
}

/// <summary>
/// Defines a contract for applying ScyllaDB schema migrations.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This interface provides a way to trigger migration application
/// programmatically, separate from the automatic startup behavior.
/// </para>
/// <para>
/// <b>Use Cases:</b>
/// <list type="bullet">
/// <item>Manual migration application via API endpoint</item>
/// <item>Testing migration behavior</item>
/// <item>Conditional migration based on runtime state</item>
/// </list>
/// </para>
/// </remarks>
public interface IScyllaSchemaMigrationService
{
    /// <summary>
    /// Applies all pending schema migrations to the database.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    Task ApplyMigrationsAsync(CancellationToken cancellationToken);
}
