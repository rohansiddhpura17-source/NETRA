namespace NETRA.Application.Abstractions;

/// <summary>
/// Outcome of a write operation. Services return a result instead of throwing for
/// expected business failures so controllers stay free of branching logic.
/// </summary>
public enum OperationStatus
{
    Success,
    NotFound,
    ValidationFailed,
    Conflict,
    Forbidden
}

public sealed class OperationResult
{
    private OperationResult(OperationStatus status, IReadOnlyDictionary<string, string[]> errors)
    {
        Status = status;
        Errors = errors;
    }

    public OperationStatus Status { get; }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public bool Succeeded => Status == OperationStatus.Success;

    public static OperationResult Success() =>
        new(OperationStatus.Success, new Dictionary<string, string[]>());

    public static OperationResult NotFound(string message = "The requested record was not found.") =>
        new(OperationStatus.NotFound, new Dictionary<string, string[]> { [""] = [message] });

    public static OperationResult Forbidden(string message = "You are not permitted to perform this action.") =>
        new(OperationStatus.Forbidden, new Dictionary<string, string[]> { [""] = [message] });

    public static OperationResult Conflict(string field, string message) =>
        new(OperationStatus.Conflict, new Dictionary<string, string[]> { [field] = [message] });

    public static OperationResult ValidationFailed(IReadOnlyDictionary<string, string[]> errors) =>
        new(OperationStatus.ValidationFailed, errors);
}

public sealed class OperationResult<T>
{
    private OperationResult(OperationStatus status, T? value, IReadOnlyDictionary<string, string[]> errors)
    {
        Status = status;
        Value = value;
        Errors = errors;
    }

    public OperationStatus Status { get; }

    public T? Value { get; }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public bool Succeeded => Status == OperationStatus.Success;

    public static OperationResult<T> Success(T value) =>
        new(OperationStatus.Success, value, new Dictionary<string, string[]>());

    public static OperationResult<T> NotFound(string message = "The requested record was not found.") =>
        new(OperationStatus.NotFound, default, new Dictionary<string, string[]> { [""] = [message] });

    public static OperationResult<T> Forbidden(string message = "You are not permitted to perform this action.") =>
        new(OperationStatus.Forbidden, default, new Dictionary<string, string[]> { [""] = [message] });

    public static OperationResult<T> ValidationFailed(IReadOnlyDictionary<string, string[]> errors) =>
        new(OperationStatus.ValidationFailed, default, errors);

    public static OperationResult<T> Conflict(string field, string message) =>
        new(OperationStatus.Conflict, default, new Dictionary<string, string[]> { [field] = [message] });
}