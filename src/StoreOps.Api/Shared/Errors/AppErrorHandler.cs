using Microsoft.AspNetCore.Diagnostics;

namespace StoreOps.Api.Shared.Errors;

/// <summary>
/// Translates exceptions into the <see cref="ErrorResponse"/> envelope. <see cref="AppError"/>s keep
/// their own code and status; anything else is an unexpected 500 whose details are logged, not returned.
/// </summary>
internal sealed partial class AppErrorHandler(ILogger<AppErrorHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var body = exception switch
        {
            ValidationError v => new ErrorBody(v.Code, v.Message, v.StatusCode, v.Field),
            AppError app => new ErrorBody(app.Code, app.Message, app.StatusCode),
            BadHttpRequestException bad => ErrorCodes.ForStatusCode(bad.StatusCode),
            _ => ErrorCodes.ForStatusCode(StatusCodes.Status500InternalServerError),
        };

        if (body.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, exception);
        }

        await ErrorEnvelope.WriteAsync(httpContext, body, cancellationToken);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception while processing request")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
