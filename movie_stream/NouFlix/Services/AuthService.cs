using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using NouFlix.DTOs;
using NouFlix.Helpers;
using NouFlix.Mapper;
using NouFlix.Models.Common;
using NouFlix.Models.Entities;
using NouFlix.Models.ValueObject;
using NouFlix.Persistence.Repositories.Interfaces;
using NouFlix.Services.Interface;
using Serilog;

namespace NouFlix.Services;

public class AuthService(
    IConfiguration configuration,
    IHttpContextAccessor accessor,
    IMinioObjectStorage storage,
    IUnitOfWork uow
    ) : IAuthService
{
    private readonly Serilog.ILogger _logger = Log.ForContext<AuthService>();

    public async Task<UserDto.UserRes> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await uow.Users.FindAsync(userId)
                   ?? throw new KeyNotFoundException("User không tồn tại.");
        return await user.ToUserResAsync(storage, ct);
    }

    public async Task RegisterAsync(RegisterReq req, CancellationToken ct = default)
    {
        var httpContext = accessor.HttpContext;
        var clientIp = httpContext?.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext?.Request.Headers["User-Agent"].ToString();
        SystemDto.AuditLog audit;
        
        if (await uow.Users.EmailExistsAsync(req.Email))
        {
            audit = new SystemDto.AuditLog
            {
                Id = Guid.NewGuid().ToString(),
                CorrelationId = (string?)httpContext?.Request.Headers["X-Correlation-Id"] ?? httpContext?.TraceIdentifier,
                UserId = null,
                Username = null,
                Action = "create",
                ResourceType = "User",
                ResourceId = null,
                Details = "UserAlreadyExists",
                ClientIp = clientIp,
                UserAgent = userAgent,
                Route = httpContext?.Request.Path.ToString(),
                HttpMethod = httpContext?.Request.Method,
                StatusCode = 200,
            };

            _logger.Warning("Auth audit {@Audit}", audit);
            
            throw new EmailAlreadyUsedException("Email đã được sử dụng.");
        }
        
        var hashed = AuthHelper.HashPassword(req.Password);
        var user = new User
        {
            Email = Email.Create(req.Email),
            Password = hashed,
        };
        await uow.Users.AddAsync(user, ct);
        await uow.SaveChangesAsync(ct);
        
        audit = new SystemDto.AuditLog
        {
            Id = Guid.NewGuid().ToString(),
            CorrelationId = (string?)httpContext?.Request.Headers["X-Correlation-Id"] ?? httpContext?.TraceIdentifier,
            UserId = user.Id.ToString(),
            Username = user.Email.ToString(),
            Action = "create",
            ResourceType = "User",
            ResourceId = user.Id.ToString(),
            Details = "RegisterSuccess",
            ClientIp = clientIp,
            UserAgent = userAgent,
            Route = httpContext?.Request.Path.ToString(),
            HttpMethod = httpContext?.Request.Method,
            StatusCode = 200,
        };

        _logger.Information("Auth audit {@Audit}", audit);
    }
    
    public async Task<AuthRes> LoginAsync(LoginReq req, CancellationToken ct = default)
    {
        var httpContext = accessor.HttpContext;
        var clientIp = httpContext?.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext?.Request.Headers["User-Agent"].ToString();

        var user = await uow.Users.GetByEmailAsync(req.Email);
        if (user is null)
        {
            var auditFailed = new SystemDto.AuditLog
            {
                Id = Guid.NewGuid().ToString(),
                CorrelationId = (string?)httpContext?.Request.Headers["X-Correlation-Id"] ?? httpContext?.TraceIdentifier,
                UserId = null,
                Username = null,
                Action = "login",
                ResourceType = "User",
                ResourceId = null,
                Details = "UserNotFound",
                ClientIp = clientIp,
                UserAgent = userAgent,
                Route = httpContext?.Request.Path.ToString(),
                HttpMethod = httpContext?.Request.Method,
                StatusCode = 401,
            };

            _logger.Warning("Auth audit {@Audit}", auditFailed);

            throw new UnauthorizedAccessException("Tài khoản không tồn tại.");
        }

        if (!AuthHelper.VerifyPassword(req.Password, user.Password))
        {
            var auditFailed = new SystemDto.AuditLog
            {
                Id = Guid.NewGuid().ToString(),
                CorrelationId = (string?)httpContext?.Request.Headers["X-Correlation-Id"] ?? httpContext?.TraceIdentifier,
                UserId = user.Id.ToString(),
                Username = user.Email.ToString(),
                Action = "login",
                ResourceType = "User",
                ResourceId = user.Id.ToString(),
                Details = "InvalidPassword",
                ClientIp = clientIp,
                UserAgent = userAgent,
                Route = httpContext?.Request.Path.ToString(),
                HttpMethod = httpContext?.Request.Method,
                StatusCode = 401,
            };

            _logger.Warning("Auth audit {@Audit}", auditFailed);

            throw new UnauthorizedAccessException("Email hoặc mật khẩu không đúng.");
        }
        
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email.ToString()),
            new Claim(ClaimTypes.Role, user.MapRole())
        };
        
        var accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var accessToken = GenerateAccessToken(claims);
        var refreshToken = GenerateRefreshToken();

        var rt = new RefreshToken
        {
            Token = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            UserId = user.Id
        };
        await uow.Refreshes.AddAsync(rt, ct);
        await uow.SaveChangesAsync(ct);
        
        var userDto = await user.ToUserResAsync(storage, ct);

        var auditSuccess = new SystemDto.AuditLog
        {
            Id = Guid.NewGuid().ToString(),
            CorrelationId = (string?)httpContext?.Request.Headers["X-Correlation-Id"] ?? httpContext?.TraceIdentifier,
            UserId = user.Id.ToString(),
            Username = user.Email.ToString(),
            Action = "login",
            ResourceType = "User",
            ResourceId = user.Id.ToString(),
            Details = "LoginSuccess",
            ClientIp = clientIp,
            UserAgent = userAgent,
            Route = httpContext?.Request.Path.ToString(),
            HttpMethod = httpContext?.Request.Method,
            StatusCode = 200,
        };

        _logger.Information("Auth audit {@Audit}", auditSuccess);

        return new AuthRes(
            accessToken,
            refreshToken,
            accessExpiresAt,
            userDto);
    }

    public Task LogoutAsync()
    {
        var httpContext = accessor.HttpContext;
        var clientIp = httpContext?.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext?.Request.Headers["User-Agent"].ToString();
        
        var userId = httpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = httpContext?.User.FindFirstValue(ClaimTypes.Email)
                    ?? httpContext?.User.Identity?.Name;
        
        var audit = new SystemDto.AuditLog
        {
            Id = Guid.NewGuid().ToString(),
            CorrelationId = (string?)httpContext?.Request.Headers["X-Correlation-Id"] ?? httpContext?.TraceIdentifier,
            UserId = userId,
            Username = email,
            Action = "logout",
            ResourceType = "User",
            ResourceId = userId,
            Details = "LogoutSuccess",
            ClientIp = clientIp,
            UserAgent = userAgent,
            Route = httpContext?.Request.Path.ToString(),
            HttpMethod = httpContext?.Request.Method,
            StatusCode = StatusCodes.Status200OK,
        };

        _logger.Information("Auth audit {@Audit}", audit);
        
        return Task.CompletedTask;
    }
    public string GenerateAccessToken(IEnumerable<Claim> claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["JWT:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: configuration["JWT:Issuer"],
            audience: configuration["JWT:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);
        
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static string GenerateRefreshToken()
    {
        var randomNumber = new Byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    public async Task<string?> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var httpContext = accessor.HttpContext;
        var clientIp = httpContext?.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext?.Request.Headers["User-Agent"].ToString();
        
        var existingToken = await uow.Refreshes.GetByTokenAsync(refreshToken);
        if (existingToken is null || existingToken.ExpiresAt < DateTime.Now || existingToken.IsRevoked)
            return null;

        // existingToken.ExpiresAt = existingToken.ExpiresAt.AddMinutes(-1);
        
        var user = existingToken.User;
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Email, user.Email.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Role, user.MapRole())
        };
        
        var newAccessToken = GenerateAccessToken(claims);
        await uow.SaveChangesAsync(ct);
        
        var audit = new SystemDto.AuditLog
        {
            Id = Guid.NewGuid().ToString(),
            CorrelationId = (string?)httpContext?.Request.Headers["X-Correlation-Id"] ?? httpContext?.TraceIdentifier,
            UserId = user.Id.ToString(),
            Username = user.Email.ToString(),
            Action = "create",
            ResourceType = "RefreshToken",
            ResourceId = user.Id.ToString(),
            Details = "RefreshTokenSuccess",
            ClientIp = clientIp,
            UserAgent = userAgent,
            Route = httpContext?.Request.Path.ToString(),
            HttpMethod = httpContext?.Request.Method,
            StatusCode = 200,
        };

        _logger.Information("Auth audit {@Audit}", audit);
        
        return newAccessToken;
    }

    public async Task<(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt)> IssueTokensForUserAsync(User user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email.ToString()),
            new Claim(ClaimTypes.Role, user.MapRole())
        };

        var accessExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var accessToken = GenerateAccessToken(claims);
        var refreshToken = GenerateRefreshToken();

        var rt = new RefreshToken
        {
            Token = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            UserId = user.Id
        };
        await uow.Refreshes.AddAsync(rt);
        await uow.SaveChangesAsync();

        return (accessToken, refreshToken, accessExpiresAt);
    }
}