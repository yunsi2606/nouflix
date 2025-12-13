using System.Security.Claims;
using NouFlix.DTOs;
using NouFlix.Models.Entities;

namespace NouFlix.Services.Interface;

public interface IAuthService
{
    Task<UserDto.UserRes> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
    Task RegisterAsync(RegisterReq req, CancellationToken ct = default);
    Task<AuthRes> LoginAsync(LoginReq req, CancellationToken ct = default);
    Task LogoutAsync();
    string GenerateAccessToken(IEnumerable<Claim> claims);
    Task<string?> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);
    Task<(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt)> IssueTokensForUserAsync(User user);
}
