using Cassandra;
using Chatify.Chat.Application.Dtos;

namespace Chatify.Chat.Infrastructure.Services.ChatHistory.Mapping;

/// <summary>
/// Defines a contract for mapping Cassandra rows to chat event DTOs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> Separates the mapping logic from the repository, making it
/// easier to test and reuse. This follows the Single Responsibility Principle.
/// </para>
/// <para>
/// <b>Design Notes:</b> Different implementations can handle various schema
/// versions or mapping strategies without modifying the repository code.
/// </para>
/// </remarks>
public interface IChatEventRowMapper
{
    /// <summary>
    /// Maps a Cassandra row to a <see cref="ChatEventDto"/>.
    /// </summary>
    /// <param name="row">
    /// The Cassandra row containing chat event data.
    /// </param>
    /// <returns>
    /// A <see cref="ChatEventDto"/> populated with data from the row.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="row"/> is null.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown when the row is missing required columns.
    /// </exception>
    ChatEventDto Map(Row row);
}
