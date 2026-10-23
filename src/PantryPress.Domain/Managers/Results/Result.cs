using System.Diagnostics.CodeAnalysis;

namespace PantryPress.Domain.Managers.Results;

/// <summary>
/// The outcome of a facade call with no value: success, or an expected <see cref="Results.Error"/>. Facades throw only
/// for bugs (backend.md). A class rather than a struct, so an unset result is null and fails loudly instead of reading
/// as success.
/// </summary>
public sealed class Result
{
    private Result(Error? error) => Error = error;

    public static Result Success { get; } = new(null);

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    public static Result Failure(Error error) => new(error ?? throw new ArgumentNullException(nameof(error)));

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>The outcome of a facade call that returns a value on success.</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T? value, Error? error)
    {
        _value = value;
        Error = error;
    }

    public Error? Error { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>The value; reading it from a failure is a bug.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"The result failed with '{Error.Code}' and has no value.");

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failure(Error error) => new(default, error ?? throw new ArgumentNullException(nameof(error)));

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
