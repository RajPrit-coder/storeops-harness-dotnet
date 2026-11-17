namespace StoreOps.Api.Shared.Errors;

/// <summary>A field value is missing or not acceptable (400).</summary>
public sealed class ValidationError(string message, string? field = null)
    : AppError(ErrorCodes.ValidationFailed, message, StatusCodes.Status400BadRequest)
{
    public string? Field { get; } = field;
}

/// <summary>The caller is not authenticated (401).</summary>
public sealed class UnauthorizedError(string message = "Authentication is required.")
    : AppError(ErrorCodes.Unauthorized, message, StatusCodes.Status401Unauthorized);

/// <summary>The caller is authenticated but not allowed to perform the action (403).</summary>
public sealed class ForbiddenError(string message)
    : AppError(ErrorCodes.Forbidden, message, StatusCodes.Status403Forbidden);

/// <summary>A referenced resource does not exist or is not visible to the caller (404).</summary>
public sealed class NotFoundError(string resource, string id)
    : AppError(ErrorCodes.NotFound, $"{resource} '{id}' was not found.", StatusCodes.Status404NotFound)
{
    public string Resource { get; } = resource;

    public string ResourceId { get; } = id;
}

/// <summary>The request conflicts with the current state of a resource (409).</summary>
public sealed class ConflictError(string message)
    : AppError(ErrorCodes.Conflict, message, StatusCodes.Status409Conflict);

/// <summary>A StoreOps business rule rejected an otherwise valid request (422).</summary>
public sealed class BusinessRuleError(string message)
    : AppError(ErrorCodes.BusinessRuleViolation, message, StatusCodes.Status422UnprocessableEntity);
