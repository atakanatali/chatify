namespace Chatify.BuildingBlocks.Primitives;

/// <summary>
/// Represents the result of an operation that can either succeed or fail, with associated error information.
/// </summary>
/// <typeparam name="T">The type of the value returned on success.</typeparam>
/// <remarks>
/// <para>
/// The <see cref="ResultEntity{T}"/> class implements the Result pattern, providing a type-safe
/// way to represent operation outcomes without relying on exceptions for control flow.
 /// This pattern encourages explicit error handling and makes error propagation more predictable.
/// </para>
/// <para>
/// Operations that can fail should return <see cref="ResultEntity{T}"/> instead of throwing
/// exceptions for expected error conditions. Exceptions should be reserved for truly
/// exceptional circumstances that cannot be reasonably handled.
/// </para>
/// <para>
/// Example usage:
/// <code><![CDATA[
/// public ResultEntity<UserEntity> GetUser(Guid userId)
/// {
///     var user = _repository.FindById(userId);
///     if (user is null)
///     {
///         return ResultEntity<UserEntity>.Failure(
///             ErrorEntity.NotFound("User", userId.ToString()));
///     }
///     return ResultEntity<UserEntity>.Success(user);
/// }
///
/// // Usage with pattern matching:
/// var result = GetUser(userId);
/// if (result.IsSuccess)
/// {
///     Console.WriteLine($"Found user: {result.Value.Name}");
/// }
/// else
/// {
///     Console.WriteLine($"Error: {result.Error.Message}");
/// }
/// ]]></code>
/// </para>
/// </remarks>
public sealed class ResultEntity<T>
{
    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    /// <value>
    /// <c>true</c> if the operation succeeded; <c>false</c> if the operation failed.
    /// </value>
    /// <remarks>
    /// When this property is <c>true</c>, <see cref="Value"/> contains the result of the operation.
    /// When <c>false</c>, <see cref="Error"/> contains information about the failure.
    /// </remarks>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    /// <value>
    /// <c>true</c> if the operation failed; <c>false</c> if the operation succeeded.
    /// </value>
    /// <remarks>
    /// This property is the logical negation of <see cref="IsSuccess"/>. It is provided
    /// for improved readability when checking for failure conditions.
    /// </remarks>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the result value when the operation succeeded.
    /// </summary>
    /// <value>
    /// The result value of type <typeparamref name="T"/>.
    /// </value>
    /// <exception cref="InvalidOperationException">
    /// Thrown when accessing this property on a failed result.
    /// </exception>
    /// <remarks>
    /// Always check <see cref="IsSuccess"/> before accessing this property.
    /// Accessing this property when <see cref="IsSuccess"/> is <c>false</c> will throw
    /// an exception to prevent using invalid data.
    /// </remarks>
    public T? Value { get; }

    /// <summary>
    /// Gets the error information when the operation failed.
    /// </summary>
    /// <value>
    /// An <see cref="ErrorEntity"/> containing details about the failure.
    /// </value>
    /// <exception cref="InvalidOperationException">
    /// Thrown when accessing this property on a successful result.
    /// </exception>
    /// <remarks>
    /// Always check <see cref="IsFailure"/> before accessing this property.
    /// Accessing this property when <see cref="IsFailure"/> is <c>false</c> will throw
    /// an exception to prevent confusing error handling logic.
    /// </remarks>
    public ErrorEntity? Error { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResultEntity{T}"/> class representing a successful result.
    /// </summary>
    /// <param name="value">The result value.</param>
    /// <remarks>
    /// This constructor creates a successful result with the specified value.
    /// The <see cref="IsSuccess"/> property is set to <c>true</c>.
    /// </remarks>
    private ResultEntity(T value)
    {
        IsSuccess = true;
        Value = value;
        Error = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResultEntity{T}"/> class representing a failed result.
    /// </summary>
    /// <param name="error">The error describing the failure.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="error"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// This constructor creates a failed result with the specified error.
    /// The <see cref="IsSuccess"/> property is set to <c>false</c>.
    /// </remarks>
    private ResultEntity(ErrorEntity error)
    {
        if (error is null)
        {
            throw new ArgumentNullException(nameof(error));
        }

        IsSuccess = false;
        Value = default;
        Error = error;
    }

    /// <summary>
    /// Creates a successful result with the specified value.
    /// </summary>
    /// <param name="value">The result value.</param>
    /// <returns>
    /// A <see cref="ResultEntity{T}"/> representing a successful operation.
    /// </returns>
    /// <remarks>
    /// This factory method provides a fluent API for creating successful results.
    /// Use this method when an operation completes without errors.
    /// </remarks>
    public static ResultEntity<T> Success(T value)
    {
        return new ResultEntity<T>(value);
    }

    /// <summary>
    /// Creates a failed result with the specified error.
    /// </summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>
    /// A <see cref="ResultEntity{T}"/> representing a failed operation.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="error"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// This factory method provides a fluent API for creating failed results.
    /// Use this method when an operation fails and you have error information to report.
    /// </remarks>
    public static ResultEntity<T> Failure(ErrorEntity error)
    {
        return new ResultEntity<T>(error);
    }

    /// <summary>
    /// Implicitly converts a value to a successful <see cref="ResultEntity{T}"/>.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>
    /// A successful <see cref="ResultEntity{T}"/> containing the specified value.
    /// </returns>
    /// <remarks>
    /// This implicit conversion allows methods to return values directly without
    /// explicitly calling <see cref="Success(T)"/>.
    /// </remarks>
    public static implicit operator ResultEntity<T>(T value)
    {
        return Success(value);
    }

    /// <summary>
    /// Executes the appropriate delegate based on whether the result succeeded or failed.
    /// </summary>
    /// <typeparam name="TResult">The type of the result returned by the delegates.</typeparam>
    /// <param name="onSuccess">The delegate to execute when the result is successful.</param>
    /// <param name="onFailure">The delegate to execute when the result failed.</param>
    /// <returns>
    /// The result of executing either <paramref name="onSuccess"/> or <paramref name="onFailure"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="onSuccess"/> or <paramref name="onFailure"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// This method provides functional-style error handling, allowing you to specify
    /// separate code paths for success and failure scenarios without explicit if statements.
    /// </remarks>
    public TResult Match<TResult>(
        Func<T, TResult> onSuccess,
        Func<ErrorEntity, TResult> onFailure)
    {
        if (onSuccess is null)
        {
            throw new ArgumentNullException(nameof(onSuccess));
        }

        if (onFailure is null)
        {
            throw new ArgumentNullException(nameof(onFailure));
        }

        return IsSuccess
            ? onSuccess(Value!)
            : onFailure(Error!);
    }

    /// <summary>
    /// Asynchronously executes the appropriate delegate based on whether the result succeeded or failed.
    /// </summary>
    /// <typeparam name="TResult">The type of the result returned by the delegates.</typeparam>
    /// <param name="onSuccess">The asynchronous delegate to execute when the result is successful.</param>
    /// <param name="onFailure">The asynchronous delegate to execute when the result failed.</param>
    /// <returns>
    /// A task representing the asynchronous operation, containing the result of executing
    /// either <paramref name="onSuccess"/> or <paramref name="onFailure"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="onSuccess"/> or <paramref name="onFailure"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// This method provides functional-style error handling for asynchronous operations,
    /// allowing you to specify separate async code paths for success and failure scenarios.
    /// </remarks>
    public async Task<TResult> MatchAsync<TResult>(
        Func<T, Task<TResult>> onSuccess,
        Func<ErrorEntity, Task<TResult>> onFailure)
    {
        if (onSuccess is null)
        {
            throw new ArgumentNullException(nameof(onSuccess));
        }

        if (onFailure is null)
        {
            throw new ArgumentNullException(nameof(onFailure));
        }

        return IsSuccess
            ? await onSuccess(Value!).ConfigureAwait(false)
            : await onFailure(Error!).ConfigureAwait(false);
    }
}

/// <summary>
/// Represents the result of an operation that can either succeed or fail, with associated error information.
/// </summary>
/// <remarks>
/// <para>
/// This non-generic version of <see cref="ResultEntity{T}"/> is used for operations that
/// do not return a value on success, such as commands, deletions, or updates where only
/// the success/failure status matters.
/// </para>
/// <para>
/// Example usage:
/// <code><![CDATA[
/// public ResultEntity DeleteUser(Guid userId)
/// {
///     var user = _repository.FindById(userId);
///     if (user is null)
///     {
///         return ResultEntity.Failure(ErrorEntity.NotFound("User", userId.ToString()));
///     }
///     _repository.Delete(user);
///     return ResultEntity.Success();
/// }
/// ]]></code>
/// </para>
/// </remarks>
public sealed class ResultEntity
{
    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    /// <value>
    /// <c>true</c> if the operation succeeded; <c>false</c> if the operation failed.
    /// </value>
    /// <remarks>
    /// When this property is <c>false</c>, <see cref="Error"/> contains information about the failure.
    /// </remarks>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    /// <value>
    /// <c>true</c> if the operation failed; <c>false</c> if the operation succeeded.
    /// </value>
    /// <remarks>
    /// This property is the logical negation of <see cref="IsSuccess"/>. It is provided
    /// for improved readability when checking for failure conditions.
    /// </remarks>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the error information when the operation failed.
    /// </summary>
    /// <value>
    /// An <see cref="ErrorEntity"/> containing details about the failure, or <c>null</c> if the operation succeeded.
    /// </value>
    /// <exception cref="InvalidOperationException">
    /// Thrown when accessing this property on a successful result.
    /// </exception>
    /// <remarks>
    /// Always check <see cref="IsFailure"/> before accessing this property.
    /// Accessing this property when <see cref="IsFailure"/> is <c>false</c> will throw
    /// an exception to prevent confusing error handling logic.
    /// </remarks>
    public ErrorEntity? Error { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResultEntity"/> class.
    /// </summary>
    /// <param name="isSuccess">Whether the operation succeeded.</param>
    /// <param name="error">The error information, if the operation failed.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="isSuccess"/> is <c>true</c> and an error is provided.
    /// </exception>
    /// <remarks>
    /// This constructor is private to enforce the use of factory methods (<see cref="Success"/>
    /// and <see cref="Failure(ErrorEntity)"/>) which provide clearer intent and prevent invalid states.
    /// </remarks>
    private ResultEntity(bool isSuccess, ErrorEntity? error = null)
    {
        if (isSuccess && error is not null)
        {
            throw new ArgumentException("Cannot provide error for successful result.", nameof(error));
        }

        if (!isSuccess && error is null)
        {
            throw new ArgumentException("Must provide error for failed result.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <returns>
    /// A <see cref="ResultEntity"/> representing a successful operation.
    /// </returns>
    /// <remarks>
    /// This factory method provides a fluent API for creating successful results
    /// for operations that don't return a value. The returned instance is cached
    /// and reused to minimize allocations.
    /// </remarks>
    public static ResultEntity Success()
    {
        return SuccessInstance;
    }

    /// <summary>
    /// Creates a failed result with the specified error.
    /// </summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>
    /// A <see cref="ResultEntity"/> representing a failed operation.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="error"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// This factory method provides a fluent API for creating failed results.
    /// Use this method when an operation fails and you have error information to report.
    /// </remarks>
    public static ResultEntity Failure(ErrorEntity error)
    {
        if (error is null)
        {
            throw new ArgumentNullException(nameof(error));
        }

        return new ResultEntity(false, error);
    }

    /// <summary>
    /// The cached singleton instance of a successful result.
    /// </summary>
    /// <remarks>
    /// This instance is reused for all successful results to minimize memory allocations,
    /// since successful results without values carry no state.
    /// </remarks>
    private static readonly ResultEntity SuccessInstance = new(isSuccess: true);

    /// <summary>
    /// Executes the appropriate delegate based on whether the result succeeded or failed.
    /// </summary>
    /// <typeparam name="TResult">The type of the result returned by the delegates.</typeparam>
    /// <param name="onSuccess">The delegate to execute when the result is successful.</param>
    /// <param name="onFailure">The delegate to execute when the result failed.</param>
    /// <returns>
    /// The result of executing either <paramref name="onSuccess"/> or <paramref name="onFailure"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="onSuccess"/> or <paramref name="onFailure"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// This method provides functional-style error handling, allowing you to specify
    /// separate code paths for success and failure scenarios without explicit if statements.
    /// </remarks>
    public TResult Match<TResult>(
        Func<TResult> onSuccess,
        Func<ErrorEntity, TResult> onFailure)
    {
        if (onSuccess is null)
        {
            throw new ArgumentNullException(nameof(onSuccess));
        }

        if (onFailure is null)
        {
            throw new ArgumentNullException(nameof(onFailure));
        }

        return IsSuccess
            ? onSuccess()
            : onFailure(Error!);
    }

    /// <summary>
    /// Asynchronously executes the appropriate delegate based on whether the result succeeded or failed.
    /// </summary>
    /// <typeparam name="TResult">The type of the result returned by the delegates.</typeparam>
    /// <param name="onSuccess">The asynchronous delegate to execute when the result is successful.</param>
    /// <param name="onFailure">The asynchronous delegate to execute when the result failed.</param>
    /// <returns>
    /// A task representing the asynchronous operation, containing the result of executing
    /// either <paramref name="onSuccess"/> or <paramref name="onFailure"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="onSuccess"/> or <paramref name="onFailure"/> is <c>null</c>.
    /// </exception>
    /// <remarks>
    /// This method provides functional-style error handling for asynchronous operations,
    /// allowing you to specify separate async code paths for success and failure scenarios.
    /// </remarks>
    public async Task<TResult> MatchAsync<TResult>(
        Func<Task<TResult>> onSuccess,
        Func<ErrorEntity, Task<TResult>> onFailure)
    {
        if (onSuccess is null)
        {
            throw new ArgumentNullException(nameof(onSuccess));
        }

        if (onFailure is null)
        {
            throw new ArgumentNullException(nameof(onFailure));
        }

        return IsSuccess
            ? await onSuccess().ConfigureAwait(false)
            : await onFailure(Error!).ConfigureAwait(false);
    }
}
