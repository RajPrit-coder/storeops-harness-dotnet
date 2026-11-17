namespace StoreOps.Api.Shared.Errors;

/// <summary>Writes the single StoreOps error envelope, whatever produced the error.</summary>
public static class ErrorEnvelope
{
    public static Task WriteAsync(HttpContext httpContext, ErrorBody body, CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = body.StatusCode;
        return httpContext.Response.WriteAsJsonAsync(new ErrorResponse(body), cancellationToken);
    }

    /// <summary>
    /// Gives framework-generated empty error responses (401/403 from auth, 404 for unknown routes,
    /// 405 for unsupported methods) the same envelope as AppErrors.
    /// </summary>
    public static IApplicationBuilder UseErrorEnvelopeForBareStatusCodes(this IApplicationBuilder app)
        => app.UseStatusCodePages(context => WriteAsync(
            context.HttpContext,
            ErrorCodes.ForStatusCode(context.HttpContext.Response.StatusCode),
            context.HttpContext.RequestAborted));
}
