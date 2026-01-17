using Cassandra;
using Chatify.Chat.Infrastructure.Options;

namespace Chatify.Chat.Infrastructure.Migrations.Chat;

/// <summary>
/// Initial schema migration for the Chat module that creates the core tables
/// required for chat message persistence.
/// </summary>
/// <remarks>
/// <para>
/// <b>Migration ID:</b> 0001_init_chat
/// </para>
/// <para>
/// <b>Module:</b> Chat
/// </para>
/// <para>
/// <b>Purpose:</b> This is the foundational migration for the Chat module that
/// establishes the database schema required for storing and querying chat messages.
/// It creates the <c>chat_messages</c> table with an optimized primary key design
/// for time-series message data.
/// </para>
/// <para>
/// <b>Keyspace Creation:</b> The keyspace is created automatically by
/// <c>AddScyllaChatify</c> during application startup (via <c>EnsureKeyspaceExistsAsync</c>),
/// before this migration runs. This ensures the keyspace exists before migrations
/// attempt to create tables within it.
/// </para>
/// <para>
/// <b>Schema Design:</b>
/// <list type="bullet">
/// <item><b>Keyspace:</b> <c>chatify</c> - Created by AddScyllaChatify with SimpleStrategy, RF=1 for development</item>
/// <item><b>Table:</b> <c>chat_messages</c> - Stores chat messages with partitioning by scope_id</item>
/// <item><b>Partition Key:</b> <c>scope_id</c> - Groups all messages in a conversation scope together</item>
/// <item><b>Clustering Keys:</b> <c>created_at_utc ASC, message_id</c> - Enables time-based ordering and unique identification</item>
/// </list>
/// </para>
/// <para>
/// <b>Query Patterns Supported:</b>
/// <list type="bullet">
/// <item>Retrieve messages for a specific scope, ordered by time</item>
/// <item>Time-range queries within a scope (from/to UTC timestamps)</item>
/// <item>Pagination through message history using LIMIT</item>
/// </list>
/// </para>
/// <para>
/// <b>Idempotency:</b> All CQL statements use <c>IF NOT EXISTS</c> clauses,
/// making this migration safe to run multiple times. The migration history table
/// ensures it's only executed once per keyspace.
/// </para>
/// <para>
/// <b>Table Options:</b>
/// <list type="bullet">
/// <item><b>gc_grace_seconds:</b> 86400 (24 hours) - Time before deleted data can be fully purged</item>
/// <item><b>compaction:</b> SizeTieredCompactionStrategy - Optimized for write-heavy workloads</item>
/// <item><b>compression:</b> LZ4Compressor - Fast compression for better storage efficiency</item>
/// </list>
/// </para>
/// <para>
/// <b>Kafka Metadata:</b> The table includes <c>broker_partition</c> and <c>broker_offset</c>
/// columns to track the Kafka position of each message. This enables:
/// <list type="bullet">
/// <item>Exactly-once processing guarantees via consumer offset management</item>
/// <item>Replay and recovery scenarios using Kafka offsets</item>
/// <item>Audit trails linking database records to event stream positions</item>
/// </list>
/// </para>
/// <para>
/// <b>Migration History:</b> Once applied, this migration is recorded in the
/// <c>schema_migrations</c> table with the composite key <c>("Chat", "0001_init_chat")</c>.
/// Subsequent runs will skip this migration.
/// </para>
/// </remarks>
public sealed class InitChatMigration : IScyllaSchemaMigration
{
    /// <inheritdoc/>
    /// <remarks>
    /// The module name identifies this migration as belonging to the Chat module.
    /// Combined with <see cref="MigrationId"/>, it forms the composite primary key
    /// in the migration history table.
    /// </remarks>
    public string ModuleName => "Chat";

    /// <inheritdoc/>
    /// <remarks>
    /// The migration ID uses a zero-padded numeric prefix (0001) to ensure
    /// correct alphabetical ordering when multiple migrations exist.
    /// This is the initial migration for the Chat module.
    /// </remarks>
    public string MigrationId => "0001_init_chat";

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>Execution:</b>
    /// <list type="number">
    /// <item>Create chat_messages table if not exists</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Table Schema:</b>
    /// The <c>chat_messages</c> table uses the following schema:
    /// <code><![CDATA[
    /// CREATE TABLE IF NOT EXISTS chatify.chat_messages (
    ///     scope_id text,
    ///     created_at_utc timestamp,
    ///     message_id uuid,
    ///     sender_id text,
    ///     text text,
    ///     origin_pod_id text,
    ///     broker_partition int,
    ///     broker_offset bigint,
    ///     PRIMARY KEY ((scope_id), created_at_utc, message_id)
    /// ) WITH CLUSTERING ORDER BY (created_at_utc ASC)
    /// AND gc_grace_seconds = 86400
    /// AND compaction = {'class': 'SizeTieredCompactionStrategy'}
    /// AND compression = {'sstable_compression': 'LZ4Compressor'};
    /// ]]></code>
    /// </para>
    /// <para>
    /// <b>Primary Key Rationale:</b>
    /// <list type="bullet">
    /// <item><c>(scope_id)</c> as partition key - Ensures all messages in a scope are stored together,
    /// enabling efficient queries for a single conversation</item>
    /// <item><c>created_at_utc ASC</c> as first clustering key - Provides chronological ordering for
    /// time-series queries and efficient time-range scans</item>
    /// <item><c>message_id</c> as second clustering key - Ensures uniqueness within the same timestamp
    /// and provides a tiebreaker for total ordering</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Error Handling:</b> Any CQL execution errors will propagate to the migration service,
    /// which will log them and either fail-fast or continue based on the
    /// <see cref="ScyllaSchemaMigrationOptionsEntity.FailFastOnSchemaError"/> setting.
    /// </para>
    /// </remarks>
    public Task ApplyAsync(ISession session, CancellationToken cancellationToken)
    {
        // Create chat_messages table if not exists
        var createTableCql = @"
            CREATE TABLE IF NOT EXISTS chatify.chat_messages (
                scope_id text,
                created_at_utc timestamp,
                message_id uuid,
                sender_id text,
                text text,
                origin_pod_id text,
                broker_partition int,
                broker_offset bigint,
                PRIMARY KEY ((scope_id), created_at_utc, message_id)
            ) WITH CLUSTERING ORDER BY (created_at_utc ASC)
            AND gc_grace_seconds = 86400
            AND compaction = {'class': 'SizeTieredCompactionStrategy'}
            AND compression = {'sstable_compression': 'LZ4Compressor'};
        ";

        var createTableStatement = new SimpleStatement(createTableCql);
        return session.ExecuteAsync(createTableStatement);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <b>Limitations:</b> ScyllaDB/Cassandra does not support transactional DDL rollback.
    /// This method is provided for development and disaster recovery scenarios where
    /// manual rollback is acceptable.
    /// </para>
    /// <para>
    /// <b>Rollback Strategy:</b>
    /// <list type="number">
    /// <item>Drop the chat_messages table (WARNING: This deletes all chat message data)</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Production Warning:</b> Never use rollback in production environments.
    /// Instead, create new migrations to fix schema issues. Rollback will result
    /// in permanent data loss.
    /// </para>
    /// <para>
    /// <b>Migration Cleanup:</b> After rollback, manually remove the migration record
    /// from the <c>schema_migrations</c> table to allow re-application:
    /// <code><![CDATA[
    /// DELETE FROM chatify.schema_migrations
    /// WHERE module_name = 'Chat' AND migration_id = '0001_init_chat';
    /// ]]></code>
    /// </para>
    /// </remarks>
    public Task RollbackAsync(ISession session, CancellationToken cancellationToken)
    {
        // Drop the table (WARNING: This deletes all data)
        var dropTableCql = "DROP TABLE IF EXISTS chatify.chat_messages;";
        var dropTableStatement = new SimpleStatement(dropTableCql);
        return session.ExecuteAsync(dropTableStatement);
    }
}
