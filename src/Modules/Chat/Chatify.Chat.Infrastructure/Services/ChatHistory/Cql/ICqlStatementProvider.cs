using Cassandra;

namespace Chatify.Chat.Infrastructure.Services.ChatHistory.Cql;

/// <summary>
/// Defines a contract for providing and caching prepared Cassandra statements.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> Prepared statements in Cassandra/ScyllaDB improve performance
/// by allowing the database to parse and plan the query once, then reuse the
/// execution plan for multiple executions. This provider caches prepared statements
/// to avoid repeated preparation overhead.
/// </para>
/// <para>
/// <b>Thread Safety:</b> Implementations must be thread-safe as the provider will
/// be used concurrently from multiple threads in the repository.
/// </para>
/// <para>
/// <b>Design Notes:</b> The provider abstracts the caching strategy, allowing
/// for different implementations (e.g., LRU cache, timed expiration) without
/// changing the repository code.
/// </para>
/// </remarks>
public interface ICqlStatementProvider
{
    /// <summary>
    /// Gets a prepared statement for the specified CQL, preparing it if necessary.
    /// </summary>
    /// <param name="cql">
    /// The CQL query string to prepare.
    /// </param>
    /// <returns>
    /// A prepared statement ready for binding and execution.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="cql"/> is null or empty.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the Cassandra session is not connected or preparation fails.
    /// </exception>
    PreparedStatement GetOrPrepare(string cql);
}
