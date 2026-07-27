using GenHTTP.Api.Content;
using GenHTTP.Api.Protocol;
using GenHTTP.Modules.Reflection;
using GenHTTP.Modules.Webservices;

namespace Server.Core;

public class AuthController
{
    private readonly IAuthService _authService;
    
    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }



    [ResourceMethod(RequestMethod.Post,"register")]
    public async ValueTask<AuthResponse> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var token = await _authService.RegisterUserAsync(request);
            return new AuthResponse { Token = token, Message = "User registered successfully" };
        }
        catch (InvalidOperationException ex)
        {
            throw new ProviderException(ResponseStatus.BadRequest, ex.Message);
        }
    }
    
    [ResourceMethod(RequestMethod.Post,"login")]
    public async ValueTask<AuthResponse> Login([FromBody] LoginRequest request)
    {
        var token = await _authService.AuthenticateAsync(request);

        if (string.IsNullOrEmpty(token))
            throw new ProviderException(ResponseStatus.Unauthorized, "Invalid credentials");

        return new AuthResponse { Token = token, Message = "Login successful" };
    }

    // Unauthenticated by design — the requester doesn't have a session yet.
    // Always responds with the same generic message regardless of whether the
    // account exists, so this can't be used to enumerate registered accounts.
    [ResourceMethod(RequestMethod.Post, "forgot-password")]
    public async ValueTask<object> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var origin = string.IsNullOrWhiteSpace(request?.Origin) ? "" : request.Origin.TrimEnd('/');
        var resetUrlBase = $"{origin}/auth/reset-password";
        var (success, message) = await _authService.RequestPasswordResetAsync(request?.UsernameOrEmail, resetUrlBase);
        return new { success, message };
    }

    [ResourceMethod(RequestMethod.Post, "reset-password")]
    public async ValueTask<object> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var (success, message) = await _authService.ResetPasswordAsync(request?.Token, request?.NewPassword);
        return new { success, message };
    }
}

public class ForgotPasswordRequest
{
    public string UsernameOrEmail { get; set; }
    // The requesting frontend's origin (window.location.origin), used to build
    // an absolute reset link — the server doesn't reliably know its own
    // public-facing URL when it's self-hosted behind an arbitrary domain/proxy.
    public string Origin { get; set; }
}

public class ResetPasswordRequest
{
    public string Token { get; set; }
    public string NewPassword { get; set; }
}
