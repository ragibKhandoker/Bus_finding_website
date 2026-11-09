namespace SmartCommute.Server.Services;

// What a service hands back to its controller: an HTTP status, a message for the user, and the value on success.
// Services hold the business rules (the model layer); controllers only turn a result into a response.
public record ServiceResult<T>(int StatusCode, string Message, T? Value = default)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;

    public static ServiceResult<T> Ok(T value, string message = "") => new(StatusCodes.Status200OK, message, value);
    public static ServiceResult<T> Created(T value, string message = "") => new(StatusCodes.Status201Created, message, value);
    public static ServiceResult<T> BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);
    public static ServiceResult<T> Unauthorized(string message) => new(StatusCodes.Status401Unauthorized, message);
    public static ServiceResult<T> Forbidden(string message = "You are not allowed to do that.") => new(StatusCodes.Status403Forbidden, message);
    public static ServiceResult<T> NotFound(string message) => new(StatusCodes.Status404NotFound, message);
    public static ServiceResult<T> Conflict(string message) => new(StatusCodes.Status409Conflict, message);
    public static ServiceResult<T> Unprocessable(string message) => new(StatusCodes.Status422UnprocessableEntity, message);

    // The same failure for a different value type.
    public ServiceResult<TOther> As<TOther>() => new(StatusCode, Message);
}
