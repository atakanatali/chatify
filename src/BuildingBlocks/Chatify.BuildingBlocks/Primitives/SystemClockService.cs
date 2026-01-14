namespace Chatify.BuildingBlocks.Primitives;

/// <summary>
/// Provides the actual system time using the <see cref="DateTime"/> and <see cref="DateTimeOffset"/> structures.
/// </summary>
/// <remarks>
/// This is the default production implementation of <see cref="IClockService"/> that returns
/// the real system time. It is registered as a singleton in the dependency injection container
/// and should be used in all production scenarios. For testing, use a mock or test double
 /// that returns fixed or controlled time values.
/// </remarks>
public sealed class SystemClockService : IClockService
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SystemClockService"/> class.
    /// </summary>
    /// <remarks>
    /// This constructor is parameterless to support singleton registration in the DI container.
    /// The service does not maintain any state and has no dependencies.
    /// </remarks>
    public SystemClockService()
    {
    }

    /// <summary>
    /// Gets the current UTC date and time from the system clock.
    /// </summary>
    /// <returns>
    /// A <see cref="DateTimeOffset"/> representing the current moment in Coordinated Universal Time (UTC).
    /// </returns>
    /// <remarks>
    /// This method delegates to <see cref="DateTimeOffset.UtcNow"/> to obtain the current time.
    /// The returned value has an offset of +00:00 (UTC) and represents the most precise
    /// time available from the system clock, typically with a resolution of approximately
    /// 10-15 milliseconds depending on the operating system and hardware.
    /// </remarks>
    public DateTimeOffset GetUtcNow()
    {
        return DateTimeOffset.UtcNow;
    }
}
