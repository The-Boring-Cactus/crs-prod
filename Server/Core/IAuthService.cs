namespace Server.Core;


public interface IAuthService
{
    Task<string> RegisterUserAsync(RegisterRequest request);
    Task<string> AuthenticateAsync(LoginRequest request);
    Task<User> GetUserAsync(string userId);
    Task<bool> ValidateTokenAsync(string token);
    string GetUserIdFromToken(string token);
    Task<bool> UpdateUserProfileAsync(string userId, string displayName);
    Task<bool> ChangePasswordAsync(string userId, string oldPassword, string newPassword);
    Task<(bool success, string message)> RequestPasswordResetAsync(string usernameOrEmail, string resetUrlBase);
    Task<(bool success, string message)> ResetPasswordAsync(string token, string newPassword);
    Task<List<User>> GetAllUsersAsync();
    Task<(bool success, string message)> UpdateUserRoleAsync(string targetUserId, string newRole);
    Task<(bool success, string message)> SetUserActiveAsync(string targetUserId, bool isActive);
}