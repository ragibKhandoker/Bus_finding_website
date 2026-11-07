using Microsoft.AspNetCore.Authorization;
using SmartCommute.Server.ViewModels;

namespace SmartCommute.Server.Extensions;

// A user signed in with a temporary password from an admin may only set a new password, check their session or
// sign out. Every other endpoint that needs sign-in answers 403 until they do. Public endpoints still work.
// The flag is read with the session check in Program.cs (JwtBearerEvents.OnTokenValidated).
public class MustChangePasswordGate(RequestDelegate next)
{
    public const string ItemKey = "MustChangePassword";
    public const string Message = "Please choose a new password to continue.";

    private static readonly string[] Allowed = ["/auth/password", "/auth/logout", "/auth/validate"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Items.ContainsKey(ItemKey)
            && context.GetEndpoint()?.Metadata.GetMetadata<IAuthorizeData>() is not null
            && !Allowed.Any(path => context.Request.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ErrorResponse(Message));
            return;
        }
        await next(context);
    }
}
