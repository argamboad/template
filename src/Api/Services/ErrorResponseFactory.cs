namespace Template.Api.Services;

/// <summary>
/// Creates standardized error response objects.
/// </summary>
public class ErrorResponseFactory : IErrorResponseFactory
{
    public object CreateError(string errorCode, string message)
    {
        return new { error = errorCode, message };
    }
}
