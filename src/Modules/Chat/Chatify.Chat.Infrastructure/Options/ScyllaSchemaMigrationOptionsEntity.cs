using Chatify.BuildingBlocks.Primitives;

namespace Chatify.Chat.Infrastructure.Options;

/// <summary>
/// Configuration options for ScyllaDB schema migration behavior in Chatify.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This options class encapsulates configuration for the code-first
/// schema migration system that manages database schema changes across deployments.
/// Schema migrations are applied during application startup to ensure the database
/// schema matches the application's expectations.
/// </para>
/// <para>
/// <b>Code-First Migrations:</b> Chatify uses a code-first migration approach where
/// each migration is a C# class implementing <see cref="Migrations.IScyllaSchemaMigration"/>.
/// Migrations are tracked in a special table (similar to <c>__EFMigrationsHistory</c>)
/// to ensure each migration is only applied once.
/// </para>
/// <para>
/// <b>Configuration Binding:</b> These options are bound from the IConfiguration
/// instance provided to the DI container. The typical configuration section is
/// "Chatify:Scylla". Example appsettings.json:
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
/// <b>Migration History Table:</b> The migration history table stores which migrations
/// have been applied, allowing the system to skip already-applied migrations on
/// subsequent startups. The table structure is:
/// <code><![CDATA[
/// CREATE TABLE IF NOT EXISTS schema_migrations (
///     migration_name text PRIMARY KEY,
///     applied_by text,
///     applied_at_utc timestamp
/// );
/// ]]></code>
/// </para>
/// <para>
/// <b>Startup Behavior:</b> When <see cref="ApplySchemaOnStartup"/> is <c>true</c>,
/// migrations are applied during application startup before the application begins
/// handling requests. This ensures schema consistency before any data operations occur.
/// </para>
/// </remarks>
public record ScyllaSchemaMigrationOptionsEntity
{
    /// <summary>
    /// Gets the name of the keyspace where migrations will be applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Default:</b> <c>"chatify"</c>
    /// </para>
    /// <para>
    /// <b>Purpose:</b> This is the target keyspace for all schema migrations.
    /// All tables, indexes, and other schema objects will be created in this keyspace.
    /// </para>
    /// <para>
    /// <b>Relationship to ScyllaOptionsEntity:</b> This value should match the
    /// <see cref="ScyllaOptionsEntity.Keyspace"/> value in the main ScyllaDB configuration.
    /// </para>
    /// </remarks>
    public string Keyspace { get; init; } = "chatify";

    /// <summary>
    /// Gets a value indicating whether schema migrations should be automatically
    /// applied during application startup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Default:</b> <c>true</c>
    /// </para>
    /// <para>
    /// <b>Purpose:</b> When enabled, the application will automatically discover and
    /// apply pending schema migrations during startup. This ensures the database schema
    /// is always up-to-date without manual intervention.
    /// </para>
    /// <para>
    /// <b>Migration Flow:</b>
    /// <list type="number">
    /// <item>Application starts</item>
    /// <item>Migration service connects to ScyllaDB</item>
    /// <item>Creates migration history table if it doesn't exist</item>
    /// <item>Queries for already-applied migrations</item>
    /// <item>Discovers all migration classes in the assembly</item>
    /// <item>Applies migrations that haven't been applied yet</item>
    /// <item>Records each applied migration in the history table</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Production Consideration:</b> In production, this should typically be
    /// <c>true</c> to ensure schema consistency across deployments. However, some
    /// organizations prefer manual migration application for tighter control over
    /// schema changes.
    /// </para>
    /// <para>
    /// <b>Development vs Production:</b>
    /// <list type="bullet">
    /// <item><b>Development:</b> Set to <c>true</c> for automatic schema updates</item>
    /// <item><b>Production:</b> Set to <c>true</c> for automated deployments, or <c>false</c> for manual control</item>
    /// </list>
    /// </para>
    /// </remarks>
    public bool ApplySchemaOnStartup { get; init; } = true;

    /// <summary>
    /// Gets the name of the table used to track applied schema migrations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Default:</b> <c>"schema_migrations"</c>
    /// </para>
    /// <para>
    /// <b>Purpose:</b> This table stores the migration history, recording which
    /// migrations have been applied, when they were applied, and which module
    /// applied them. The migration service checks this table to skip migrations
    /// that have already been applied.
    /// </para>
    /// <para>
    /// <b>Table Schema:</b>
    /// <code><![CDATA[
    /// CREATE TABLE IF NOT EXISTS schema_migrations (
    ///     migration_name text PRIMARY KEY,
    ///     applied_by text,
    ///     applied_at_utc timestamp
    /// );
    /// ]]></code>
    /// </para>
    /// <para>
    /// <b>Table Columns:</b>
    /// <list type="bullet">
    /// <item><c>migration_name</c>: The unique name of the migration (primary key)</item>
    /// <item><c>applied_by</c>: The module/assembly that applied the migration</item>
    /// <item><c>applied_at_utc</c>: The timestamp when the migration was applied</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Similarity to EF Core:</b> This table is similar to <c>__EFMigrationsHistory</c>
    /// in Entity Framework Core, but is specific to Chatify's code-first migration system.
    /// </para>
    /// <para>
    /// <b>Naming Considerations:</b> The table name should be:
    /// <list type="bullet">
    /// <item>Distinct from application tables to avoid conflicts</item>
    /// <item>Easy to identify as a system table (hence the <c>schema_</c> prefix)</item>
    /// <item>Consistent across environments</item>
    /// </list>
    /// </para>
    /// </remarks>
    public string SchemaMigrationTableName { get; init; } = "schema_migrations";

    /// <summary>
    /// Gets a value indicating whether the application should fail to start
    /// if a schema migration encounters an error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Default:</b> <c>true</c>
    /// </para>
    /// <para>
    /// <b>Purpose:</b> When enabled, any error during schema migration will cause
    /// the application startup to fail. This fail-fast behavior ensures that schema
    /// inconsistencies are detected immediately rather than allowing the application
    /// to run with an incorrect schema.
    /// </para>
    /// <para>
    /// <b>Behavior When True:</b>
    /// <list type="bullet">
    /// <item>First migration error throws an exception</item>
    /// <item>Application startup is aborted</item>
    /// <item>Logs the error with full details</item>
    /// <item>Kubernetes/orchestrator will restart the pod</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Behavior When False:</b>
    /// <list type="bullet">
    /// <item>Migration errors are logged but don't stop startup</item>
    /// <item>Remaining migrations continue to apply</item>
    /// <item>Application starts even if some migrations failed</item>
    /// <item>Risk: Application may run with partial/inconsistent schema</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Recommendation:</b> Keep this as <c>true</c> in production to ensure
    /// schema consistency. Only set to <c>false</c> for development scenarios
    /// where you want to apply as many migrations as possible despite errors.
    /// </para>
    /// <para>
    /// <b>Error Logging:</b> Regardless of this setting, all migration errors
    /// are logged with full details including:
    /// <list type="bullet">
    /// <item>Migration name</item>
    /// <item>CQL statement that failed</item>
    /// <item>Exception message and stack trace</item>
    /// <item>Timestamp</item>
    /// </list>
    /// </para>
    /// </remarks>
    public bool FailFastOnSchemaError { get; init; } = true;

    /// <summary>
    /// Validates the schema migration options configuration.
    /// </summary>
    /// <returns>
    /// <c>true</c> if all required fields are present and valid; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Validation Rules:</b>
    /// <list type="bullet">
    /// <item><see cref="Keyspace"/> is not null or whitespace</item>
    /// <item><see cref="SchemaMigrationTableName"/> is not null or whitespace</item>
    /// <item><see cref="SchemaMigrationTableName"/> is a valid CQL identifier</item>
    /// </list>
    /// </para>
    /// <para>
    /// This method is called by the DI extension when registering schema migration
    /// services. If validation fails, an <see cref="ArgumentException"/> is thrown
    /// during service registration to fail fast before the application starts.
    /// </para>
    /// </remarks>
    public bool IsValid()
    {
        // Validate keyspace
        if (string.IsNullOrWhiteSpace(Keyspace))
        {
            return false;
        }

        // Validate migration table name
        if (string.IsNullOrWhiteSpace(SchemaMigrationTableName))
        {
            return false;
        }

        // Ensure table name is a valid identifier (no special characters except underscore)
        var tableName = SchemaMigrationTableName.Trim();
        if (tableName.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns a string representation of the schema migration options for logging purposes.
    /// </summary>
    /// <returns>
    /// A string containing the key configuration properties.
    /// </returns>
    /// <remarks>
    /// This method is useful for logging the schema migration configuration on startup
    /// to help operators understand the migration behavior.
    /// </remarks>
    public override string ToString()
    {
        return $"ScyllaSchemaMigrationOptionsEntity {{ Keyspace = {Keyspace}, ApplySchemaOnStartup = {ApplySchemaOnStartup}, SchemaMigrationTableName = {SchemaMigrationTableName}, FailFastOnSchemaError = {FailFastOnSchemaError} }}";
    }
}
