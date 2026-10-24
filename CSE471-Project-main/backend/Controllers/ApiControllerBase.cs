using Microsoft.AspNetCore.Mvc;
using SmartCommute.Server.Extensions;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected Guid CurrentUserId => User.GetUserId();
    protected bool IsAdmin => User.IsAdmin();
    protected bool IsSignedIn => User.Identity?.IsAuthenticated == true;

    protected ObjectResult Error(int statusCode, string message) => StatusCode(statusCode, new ErrorResponse(message));
    protected ObjectResult BadRequestError(string message) => Error(StatusCodes.Status400BadRequest, message);
    protected ObjectResult NotFoundError(string message) => Error(StatusCodes.Status404NotFound, message);
    protected ObjectResult ForbiddenError(string message = "You are not allowed to do that.") => Error(StatusCodes.Status403Forbidden, message);

    protected ObjectResult Created201(object body) => StatusCode(StatusCodes.Status201Created, body);

    // A signed-in caller may only act as themselves (admins excepted). Anonymous callers are trusted
    // for the endpoints the existing clients call without a token.
    protected bool CanActAs(Guid userId) => !IsSignedIn || IsAdmin || CurrentUserId == userId;
}
