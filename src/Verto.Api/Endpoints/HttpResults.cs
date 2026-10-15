using Verto.Application.Common;

namespace Verto.Api.Endpoints;

public static class HttpResults
{
    public static IResult ToHttpResult<T>(OperationResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        var status = result.ErrorCode switch
        {
            "email_already_registered" => StatusCodes.Status409Conflict,
            "invalid_credentials" => StatusCodes.Status401Unauthorized,
            "invalid_refresh_token" => StatusCodes.Status401Unauthorized,
            "signup_disabled" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        };

        return Results.Json(new { error = result.ErrorCode, message = result.ErrorMessage }, statusCode: status);
    }
}
