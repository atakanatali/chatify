using Cassandra;

namespace Chatify.Chat.Infrastructure.Migrations;

/// <summary>
/// Defines a contract for ScyllaDB/Cassandra schema migrations in Chatify.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> This interface provides a contract for code-first schema migrations
/// that can be applied to ScyllaDB during application startup or via explicit invocation.
/// Each migration represents a single schema change and is responsible for both
/// applying and rolling back the change.
/// </para>
/// <para>
/// <b>Code-First Migrations:</b> Unlike EF Core migrations which use .cql files or
/// fluent APIs, Chatify migrations are implemented as C# classes that execute CQL
/// statements directly. This approach provides:
/// <list type="bullet">
/// <item>Compile-time safety and testability</item>
/// <item>Version control integration with code</item>
/// <item>Ability to use C# logic for conditional migrations</item>
/// <item>No external tooling requirements beyond the Cassandra driver</item>
/// </list>
/// </para>
/// <para>
/// <b>Migration History:</b> Applied migrations are tracked in a special table
/// (similar to <c>__EFMigrationsHistory</c>) to ensure each migration is only
/// applied once. The table name is configurable via
/// <see cref="Options.ScyllaSchemaMigrationOptionsEntity"/>.
/// </para>
/// <para>
/// <b>Module Ownership:</b> Each module/domain owns its migrations in the
/// <c>Migrations/{ModuleName}/</c> folder within its Infrastructure project.
/// This ensures clear ownership and separation of concerns.
/// </para>
/// <para>
/// <b>Composite Migration Key:</b> Migrations are uniquely identified by the
/// combination of <see cref="ModuleName"/> and <see cref="MigrationId"/>. This allows
/// different modules to have migrations with the same migration ID without conflicts.
/// The migration history table uses a composite primary key: <c>(module_name, migration_id)</c>.
/// </para>
/// <para>
/// <b>Implementation Example:</b>
/// <code><![CDATA[
/// public class V001_CreateChatMessagesTable : IScyllaSchemaMigration
/// {
///     public string ModuleName => "Chatify.Chat.Infrastructure";
///     public string MigrationId => "V001_CreateChatMessagesTable";
///
///     public Task ApplyAsync(ISession session, CancellationToken cancellationToken)
///     {
///         var cql = @"
///             CREATE TABLE IF NOT EXISTS chat_messages (
///                 scope_id text,
///                 created_at_utc timestamp,
///                 message_id uuid,
///                 sender_id text,
///                 text text,
///                 origin_pod_id text,
///                 broker_partition int,
///                 broker_offset bigint,
///                 PRIMARY KEY ((scope_id), created_at_utc, message_id)
///             ) WITH CLUSTERING ORDER BY (created_at_utc ASC);
///         ";
///         return session.ExecuteAsync(new SimpleStatement(cql), cancellationToken);
///     }
/// }
/// ]]></code>
/// </para>
/// <para>
/// <b>Rollback Support:</b> While ScyllaDB doesn't support transactional DDL rollback,
/// the <see cref="RollbackAsync"/> method can be implemented to provide manual rollback
/// capabilities for development environments or disaster recovery scenarios.
/// </para>
/// </remarks>
public interface IScyllaSchemaMigration
{
    /// <summary>
    /// Gets the identifier of the module or domain that owns this migration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Format:</b> Typically the assembly name or a logical module name.
    /// Examples: <c>Chatify.Chat.Infrastructure</c>, <c>Chatify.Users.Infrastructure</c>.
    /// </para>
    /// <para>
    /// <b>Purpose:</b> This field helps track which migrations belong to which module
    /// in a modular monolith architecture. Combined with <see cref="MigrationId"/>,
    /// it forms the composite primary key in the migration history table.
    /// </para>
    /// <para>
    /// <b>Composite Key:</b> The migration history table uses <c>(module_name, migration_id)</c>
    /// as the primary key, allowing different modules to have migrations with the same
    /// migration ID without conflicts.
    /// </para>
    /// </remarks>
    string ModuleName { get; }

    /// <summary>
    /// Gets the unique identifier of this migration within its module.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Uniqueness Scope:</b> This ID must be unique within <see cref="ModuleName"/>,
    /// but different modules can have migrations with the same ID without conflicts
    /// because the migration history table uses a composite primary key.
    /// </para>
    /// <para>
    /// <b>Format:</b> A common pattern is to use a version prefix followed by a
    /// descriptive name: <c>V001_Description</c>, <c>V002_AddIndex</c>, etc.
    /// </para>
    /// <para>
    /// <b>Migration Tracking:</b> The combination of <c>(ModuleName, MigrationId)</c>
    /// is stored in the migration history table when the migration is applied.
    /// The migration service checks this table to skip migrations that have already
    /// been applied.
    /// </para>
    /// <para>
    /// <b>Examples:</b>
    /// <list type="bullet">
    /// <item><c>V001_CreateChatMessagesTable</c></item>
    /// <item><c>V002_CreateSchemaMigrationsTable</c></item>
    /// <item><c>V003_AddUserPresenceTable</c></item>
    /// </list>
    /// </para>
    /// </remarks>
    string MigrationId { get; }

    /// <summary>
    /// Applies the schema changes to the database.
    /// </summary>
    /// <param name="session">
    /// The Cassandra/ScyllaDB session connected to the keyspace.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Execution:</b> This method should execute one or more CQL statements
    /// to create or modify database schema objects (tables, indexes, types, etc.).
    /// </para>
    /// <para>
    /// <b>Best Practices:</b>
    /// <list type="bullet">
    /// <item>Use <c>IF NOT EXISTS</c> clauses where possible for idempotency</item>
    /// <item>Use prepared statements for repetitive operations</item>
    /// <item>Avoid multiple schema changes in a single migration when possible</item>
    /// <item>Test migrations in development before applying to production</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Error Handling:</b> Exceptions thrown by this method will be caught by
    /// the migration service. Depending on the
    /// <see cref="Options.ScyllaSchemaMigrationOptionsEntity.FailFastOnSchemaError"/> setting,
    /// the service may either stop or continue with remaining migrations.
    /// </para>
    /// <para>
    /// <b>Consistency:</b> Schema operations should use <c>LOCAL_QUORUM</c> or higher
    /// consistency level to ensure changes are propagated to replicas.
    /// </para>
    /// </remarks>
    Task ApplyAsync(ISession session, CancellationToken cancellationToken);

    /// <summary>
    /// Rolls back the schema changes from the database.
    /// </summary>
    /// <param name="session">
    /// The Cassandra/ScyllaDB session connected to the keyspace.
    /// </param>
    /// <param name="cancellationToken">
    /// A token to monitor for cancellation requests.
    /// </param>
    /// <returns>
    /// A task representing the asynchronous operation.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Limitations:</b> ScyllaDB/Cassandra does not support transactional DDL rollback.
    /// This method is provided for development and disaster recovery scenarios where
    /// manual rollback is acceptable.
    /// </para>
    /// <para>
    /// <b>Implementation:</b> This method should execute CQL statements that reverse
    /// the changes made by <see cref="ApplyAsync"/>. For example:
    /// <list type="bullet">
    /// <item><c>DROP TABLE IF EXISTS table_name;</c> to undo a table creation</item>
    /// <item><c>DROP INDEX IF EXISTS index_name;</c> to undo an index creation</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Production Consideration:</b> In production, prefer creating new migrations
    /// to fix issues rather than rolling back, as rollback may result in data loss
    /// if the schema has been in use.
    /// </para>
    /// <para>
    /// <b>Default Implementation:</b> If rollback is not supported, this method should
    /// throw <see cref="NotImplementedException"/>.
    /// </para>
    /// </remarks>
    Task RollbackAsync(ISession session, CancellationToken cancellationToken);
}
