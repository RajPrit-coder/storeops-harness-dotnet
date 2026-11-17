namespace StoreOps.Api.Shared.Errors;

public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string BadRequest = "BAD_REQUEST";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string RequestTimeout = "REQUEST_TIMEOUT";
    public const string Conflict = "CONFLICT";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";
    public const string BusinessRuleViolation = "BUSINESS_RULE_VIOLATION";
    public const string TooManyRequests = "TOO_MANY_REQUESTS";
    public const string ClientError = "CLIENT_ERROR";
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>
    /// Builds the envelope for a status code the framework produced (no AppError involved). The code always
    /// agrees with the status: an unlisted 4xx is CLIENT_ERROR, never a misleading BAD_REQUEST.
    /// </summary>
    public static ErrorBody ForStatusCode(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => new(BadRequest, "The request body or parameters are malformed.", statusCode),
        StatusCodes.Status401Unauthorized => new(Unauthorized, "Authentication is required.", statusCode),
        StatusCodes.Status403Forbidden => new(Forbidden, "You are not allowed to perform this action.", statusCode),
        StatusCodes.Status404NotFound => new(NotFound, "The requested resource does not exist.", statusCode),
        StatusCodes.Status405MethodNotAllowed => new(MethodNotAllowed, "This HTTP method is not supported for this resource.", statusCode),
        StatusCodes.Status408RequestTimeout => new(RequestTimeout, "The request took too long to arrive.", statusCode),
        StatusCodes.Status413PayloadTooLarge => new(PayloadTooLarge, "The request body is too large.", statusCode),
        StatusCodes.Status415UnsupportedMediaType => new(UnsupportedMediaType, "Send the request body as application/json.", statusCode),
        StatusCodes.Status429TooManyRequests => new(TooManyRequests, "Too many requests.", statusCode),
        >= 400 and < 500 => new(ClientError, "The request could not be processed.", statusCode),
        _ => new(InternalError, "An unexpected error occurred.", StatusCodes.Status500InternalServerError),
    };
}
