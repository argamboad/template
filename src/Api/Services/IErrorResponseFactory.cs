namespace Template.Api.Services;

/// <summary>
/// Creates standardized error response objects.
/// </summary>
public interface IErrorResponseFactory
{
    object CreateError(string errorCode, string message);
}
