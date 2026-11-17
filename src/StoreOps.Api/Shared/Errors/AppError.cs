namespace StoreOps.Api.Shared.Errors;

/// <summary>
/// Base class for every error StoreOps raises on purpose (spec §3.5 "Error contract").
/// Services and routes throw a subtype of this class, never a raw <see cref="Exception"/>.
/// Enforced by the banned-API analyzer (BannedSymbols.txt) and by ErrorContractTests.
/// </summary>
public abstract class AppError : Exception
{
#pragma warning disable RS0030 // The one sanctioned call of the System.Exception constructor.
    protected AppError(string code, string message, int statusCode)
        : base(message)
#pragma warning restore RS0030
    {
        Code = code;
        StatusCode = statusCode;
    }

    /// <summary>Gets the stable, machine-readable error code, e.g. <c>NOT_FOUND</c>.</summary>
    public string Code { get; }

    /// <summary>Gets the HTTP status code the error maps to.</summary>
    public int StatusCode { get; }
}
