using System.Text.Json.Serialization;

namespace StoreOps.Api.Shared.Errors;

/// <summary>Wire format of every error response: <c>{ "error": { code, message, statusCode, field? } }</c>.</summary>
public sealed record ErrorResponse(ErrorBody Error);

public sealed record ErrorBody(
    string Code,
    string Message,
    int StatusCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Field = null);
