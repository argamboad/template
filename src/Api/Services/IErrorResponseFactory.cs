namespace Template.Api.Services;

/// <summary>The standard API error envelope. Serializes to <c>{ "error": ..., "message": ... }</c>
/// (camelCase) — the shape every error response uses.</summary>
public record ErrorResponse(string Error, string Message);

/// <summary>
/// Creates standardized <see cref="ErrorResponse"/> envelopes.
/// </summary>
public interface IErrorResponseFactory
{
    ErrorResponse CreateError(string errorCode, string message);
}
