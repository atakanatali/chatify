namespace Chatify.BuildingBlocks.Primitives;

/// <summary>
/// Defines a contract for obtaining the current time, allowing for deterministic time handling
/// in unit tests and scenarios requiring time abstraction.
/// </summary>
/// <remarks>
/// This abstraction enables testing of time-dependent logic by providing a controllable
/// time source rather than relying on the system clock. Implementations can return
/// fixed or adjusted time values for testing scenarios.
/// </remarks>
public interface IClockService
{
    /// <summary>
    /// Gets the current UTC date and time.
    /// </summary>
    /// <returns>
    /// A <see cref="DateTimeOffset"/> representing the current moment in Coordinated Universal Time (UTC).
    /// </returns>
    /// <remarks>
    /// The returned value includes an offset of zero for UTC. Implementations should ensure
    /// that the returned value is in UTC regardless of the system's local time zone.
    /// </remarks>
    DateTimeOffset GetUtcNow();
}
