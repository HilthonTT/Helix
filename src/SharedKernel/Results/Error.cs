namespace SharedKernel.Results;

public record Error
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);
    public static readonly Error NullValue = new(
        "General.Null",
        "Null value was provided",
        ErrorType.Failure);

    public static readonly Error UnProcessableRequest = new(
        "General.UnProcessableRequest",
        "The server could not process the request",
        ErrorType.Problem);

    public Error(string code, string description, ErrorType type, params object?[] arguments)
    {
        Code = code;
        Description = description;
        Type = type;
        Arguments = arguments;
    }

    public string Code { get; }

    public string Description { get; }

    public ErrorType Type { get; }

    public IReadOnlyList<object?> Arguments { get; }

    public virtual bool Equals(Error? other) =>
        other is not null &&
        EqualityContract == other.EqualityContract &&
        Code == other.Code &&
        Description == other.Description &&
        Type == other.Type;

    public override int GetHashCode() => HashCode.Combine(EqualityContract, Code, Description, Type);

    public static Error Failure(string code, string description, params object?[] arguments) =>
        new(code, description, ErrorType.Failure, arguments);

    public static Error NotFound(string code, string description, params object?[] arguments) =>
        new(code, description, ErrorType.NotFound, arguments);

    public static Error Problem(string code, string description, params object?[] arguments) =>
        new(code, description, ErrorType.Problem, arguments);

    public static Error Conflict(string code, string description, params object?[] arguments) =>
        new(code, description, ErrorType.Conflict, arguments);

    public static Error Unauthorized(string code, string description, params object?[] arguments) =>
        new(code, description, ErrorType.Unauthorized, arguments);
}
