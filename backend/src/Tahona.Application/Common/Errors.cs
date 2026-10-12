namespace Tahona.Application.Common;

/// <summary>Base for expected, user-facing failures. Mapped to RFC 7807 problem responses by the API.</summary>
public abstract class AppException(string message) : Exception(message)
{
    public abstract int StatusCode { get; }
}

public sealed class NotFoundException(string entity, object? key = null)
    : AppException(key is null ? $"{entity} was not found." : $"{entity} '{key}' was not found.")
{
    public override int StatusCode => 404;
}

public sealed class BusinessRuleException(string message) : AppException(message)
{
    public override int StatusCode => 422;
}

public sealed class ConflictException(string message) : AppException(message)
{
    public override int StatusCode => 409;
}

public sealed class ForbiddenException(string message = "You do not have permission to perform this action.")
    : AppException(message)
{
    public override int StatusCode => 403;
}

public sealed class UnauthorizedException(string message = "Authentication is required.") : AppException(message)
{
    public override int StatusCode => 401;
}

/// <summary>Validation failure with per-field messages (field name → messages).</summary>
public sealed class RequestValidationException(IDictionary<string, string[]> errors)
    : AppException("One or more validation errors occurred.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;
    public override int StatusCode => 400;

    public static RequestValidationException For(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
