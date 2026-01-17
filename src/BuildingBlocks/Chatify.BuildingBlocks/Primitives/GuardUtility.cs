using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Chatify.BuildingBlocks.Primitives;

/// <summary>
/// Provides guard clauses and defensive programming utilities for validating method arguments and state.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="GuardUtility"/> class encapsulates common validation patterns used throughout
/// the application, providing a consistent approach to argument validation and error reporting.
/// All methods throw <see cref="ArgumentException"/> or its derived types when validation fails.
/// </para>
/// <para>
/// This class is static and cannot be instantiated. All methods are designed to be called
/// directly from the type, providing a fluent and readable validation syntax.
/// </para>
/// <para>
/// Example usage:
/// <code><![CDATA[
/// public void ProcessUser(UserEntity user, string name)
/// {
///     GuardUtility.NotNull(user);
///     GuardUtility.NotEmpty(name);
///     GuardUtility.InRange(user.Age, 18, 120);
///     // Method logic continues...
/// }
/// ]]></code>
/// </para>
/// </remarks>
public static class GuardUtility
{
    /// <summary>
    /// Validates that the specified argument is not <c>null</c>.
    /// </summary>
    /// <typeparam name="T">The type of the argument to validate.</typeparam>
    /// <param name="value">The argument value to validate.</param>
    /// <param name="paramName">The name of the parameter being validated. Automatically populated by the compiler.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// This method validates reference types, nullable value types, and strings.
    /// For non-nullable value types, this method will never throw as they cannot be <c>null</c>.
    /// The compiler automatically provides the parameter name via caller info attributes.
    /// </remarks>
    public static void NotNull<T>(
        [NotNull] T? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : class
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }
    }

    /// <summary>
    /// Validates that the specified string argument is not <c>null</c> or empty.
    /// </summary>
    /// <param name="value">The string argument value to validate.</param>
    /// <param name="paramName">The name of the parameter being validated. Automatically populated by the compiler.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is <c>null</c> or an empty string ("").
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method only checks for <c>null</c> or empty strings. Strings containing only whitespace
    /// are considered valid. Use <see cref="string.IsNullOrWhiteSpace(string)"/> validation if whitespace-only
    /// strings should be rejected.
    /// </para>
    /// <para>
    /// This method throws <see cref="ArgumentException"/> rather than <see cref="ArgumentNullException"/>
    /// to distinguish between values that must have content versus values that must simply exist.
    /// </para>
    /// </remarks>
    public static void NotEmpty(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("String value cannot be null or empty.", paramName);
        }
    }

    /// <summary>
    /// Validates that the specified string argument is not <c>null</c>, empty, or consisting only of whitespace characters.
    /// </summary>
    /// <param name="value">The string argument value to validate.</param>
    /// <param name="paramName">The name of the parameter being validated. Automatically populated by the compiler.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is <c>null</c>, an empty string (""), or contains only whitespace characters.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Purpose:</b> This method provides strict string validation that rejects not only <c>null</c> and empty strings
    /// but also strings that contain only whitespace characters (spaces, tabs, newlines, etc.).
    /// </para>
    /// <para>
    /// <b>Use Cases:</b> Use this validation for strings that must contain meaningful content, such as:
    /// <list type="bullet">
    /// <item>User names and identifiers</item>
    /// <item>Database keyspace and table names</item>
    /// <item>Configuration values that must be non-empty</item>
    /// <item>URLs and paths</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Whitespace Characters:</b> This method uses <see cref="string.IsNullOrWhiteSpace(string)"/>,
    /// which considers the following characters as whitespace: spaces, tabs, line feeds, carriage returns,
    /// form feeds, vertical tabs, and any other Unicode characters categorized as whitespace.
    /// </para>
    /// <para>
    /// <b>Comparison:</b> This method is stricter than <see cref="NotEmpty(string?, string?)"/>, which
    /// allows whitespace-only strings. Choose the appropriate method based on whether whitespace-only
    /// strings should be considered valid for your use case.
    /// </para>
    /// </remarks>
    public static void NotNullOrWhiteSpace(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("String value cannot be null, empty, or whitespace.", paramName);
        }
    }

    /// <summary>
    /// Validates that the specified collection argument is not <c>null</c> or empty.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection.</typeparam>
    /// <param name="value">The collection argument value to validate.</param>
    /// <param name="paramName">The name of the parameter being validated. Automatically populated by the compiler.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is <c>null</c> or contains no elements.
    /// </exception>
    /// <remarks>
    /// This method validates any collection implementing <see cref="IEnumerable{T}"/>, including arrays,
    /// lists, and other collection types. The enumeration is not performed; only the count is checked
    /// for efficiency. LINQ's
    /// is used for the empty check, which may have performance implications for certain collection types.
    /// </remarks>
    public static void NotEmpty<T>(
        [NotNull] IEnumerable<T>? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value is null || !value.Any())
        {
            throw new ArgumentException("Collection value cannot be null or empty.", paramName);
        }
    }

    /// <summary>
    /// Validates that the specified integer value falls within the specified inclusive range.
    /// </summary>
    /// <param name="value">The integer value to validate.</param>
    /// <param name="min">The minimum allowed value (inclusive).</param>
    /// <param name="max">The maximum allowed value (inclusive).</param>
    /// <param name="paramName">The name of the parameter being validated. Automatically populated by the compiler.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="value"/> is less than <paramref name="min"/> or greater than <paramref name="max"/>.
    /// </exception>
    /// <remarks>
    /// The range boundaries are inclusive, meaning values equal to <paramref name="min"/> or <paramref name="max"/>
    /// are considered valid. This method uses the standard comparison operators and is optimized
    /// for primitive integer types.
    /// </remarks>
    public static void InRange(
        int value,
        int min,
        int max,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(paramName, $"Value must be between {min} and {max} (inclusive), but was {value}.");
        }
    }

    /// <summary>
    /// Validates that the specified decimal value falls within the specified inclusive range.
    /// </summary>
    /// <param name="value">The decimal value to validate.</param>
    /// <param name="min">The minimum allowed value (inclusive).</param>
    /// <param name="max">The maximum allowed value (inclusive).</param>
    /// <param name="paramName">The name of the parameter being validated. Automatically populated by the compiler.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="value"/> is less than <paramref name="min"/> or greater than <paramref name="max"/>.
    /// </exception>
    /// <remarks>
    /// This overload supports <see cref="decimal"/> values for scenarios requiring high precision,
    /// such as financial calculations. The range boundaries are inclusive.
    /// </remarks>
    public static void InRange(
        decimal value,
        decimal min,
        decimal max,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(paramName, $"Value must be between {min} and {max} (inclusive), but was {value}.");
        }
    }

    /// <summary>
    /// Validates that the specified double value falls within the specified inclusive range.
    /// </summary>
    /// <param name="value">The double value to validate.</param>
    /// <param name="min">The minimum allowed value (inclusive).</param>
    /// <param name="max">The maximum allowed value (inclusive).</param>
    /// <param name="paramName">The name of the parameter being validated. Automatically populated by the compiler.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="value"/> is less than <paramref name="min"/> or greater than <paramref name="max"/>.
    /// </exception>
    /// <remarks>
    /// This overload supports <see cref="double"/> values for scientific calculations and measurements.
    /// The range boundaries are inclusive. Be aware of floating-point precision issues when comparing
    /// values near the boundaries.
    /// </remarks>
    public static void InRange(
        double value,
        double min,
        double max,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(paramName, $"Value must be between {min} and {max} (inclusive), but was {value}.");
        }
    }

    /// <summary>
    /// Validates that the specified <see cref="DateTime"/> value falls within the specified inclusive range.
    /// </summary>
    /// <param name="value">The DateTime value to validate.</param>
    /// <param name="min">The minimum allowed value (inclusive).</param>
    /// <param name="max">The maximum allowed value (inclusive).</param>
    /// <param name="paramName">The name of the parameter being validated. Automatically populated by the compiler.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="value"/> is less than <paramref name="min"/> or greater than <paramref name="max"/>.
    /// </exception>
    /// <remarks>
    /// This overload is useful for validating date ranges, such as ensuring a birth date is not in the future
    /// or an expiration date is not in the past. The comparison is performed using standard DateTime operators.
    /// </remarks>
    public static void InRange(
        DateTime value,
        DateTime min,
        DateTime max,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(paramName, $"Value must be between {min:O} and {max:O} (inclusive), but was {value:O}.");
        }
    }
}
