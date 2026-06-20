namespace Template.Api.Services;

/// <summary>
/// Creates standardized error response objects.
/// </summary>
public class ErrorResponseFactory : IErrorResponseFactory
{
    public ErrorResponse CreateError(string errorCode, string message) => new(errorCode, message);
}
