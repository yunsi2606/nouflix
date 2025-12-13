using System.Security.Claims;
using FluentValidation;
using Microsoft.Extensions.Options;
using NouFlix.DTOs;
using NouFlix.Helpers;
using NouFlix.Mapper;
using NouFlix.Models.Common;
using NouFlix.Models.Entities;
using NouFlix.Models.Specification;
using NouFlix.Models.ValueObject;
using NouFlix.Persistence.Repositories.Interfaces;
using NouFlix.Services.Interface;

namespace NouFlix.Services;

public class UserService(
    IUnitOfWork uow,
    MinioObjectStorage storage,
    IOptions<StorageOptions> opt) : IUserService
{
    private const long MaxSize = 5 * 1024 * 1024;
    
    public async Task<SearchRes<IEnumerable<UserDto.UserRes>>> SearchAsync(
        string? q, int skip, int take, CancellationToken ct = default)
    {
        // Bảo vệ tham số
        skip = Math.Max(1, skip);
        take = Math.Clamp(take, 1, 200);

        var total = await uow.Users.CountAsync(q, skip, take, ct);
        var users = await uow.Users.SearchAsync(q, skip, take, ct);
        var res = await Task.WhenAll(
            users.Select(u => u.ToUserResAsync(storage, ct))
        );

        return new SearchRes<IEnumerable<UserDto.UserRes>>(
            Count: total,
            Data: res
        );
    }

    public async Task<IEnumerable<HistoryDto.Item>> GetHistory(Guid userId, CancellationToken ct)
        => await (await uow.Histories.GetByUserAsync(userId, ct)).ToItemListResAsync(ct);
    
    public async Task<User> FindOrCreateExternal(string provider, string providerKey, string? email, string? avatar, ClaimsPrincipal principal)
    {
        User? user = null;
        if (!string.IsNullOrWhiteSpace(email))
        {
            user = await uow.Users.GetByEmailAsync(email);
            if (user is not null) return user;
        }
        
        if (string.IsNullOrWhiteSpace(email))
            throw new ValidationException("Provider không trả về email. Vui lòng bật email public hoặc đăng nhập cách khác.");
        
        var newUser = new User
        {
            Email = Email.Create(email),
            // Password: SSO không dùng → đặt random
            Password = AuthHelper.HashPassword(Guid.NewGuid().ToString("N")),
        };
        
        var givenName = principal.FindFirst(ClaimTypes.GivenName)?.Value ?? "";
        var surname = principal.FindFirst(ClaimTypes.Surname)?.Value ?? "";
        var fullName = principal.FindFirst(ClaimTypes.Name)?.Value ?? "";

        // Nếu thiếu given/surname thì tách từ full name
        if (string.IsNullOrWhiteSpace(givenName) && string.IsNullOrWhiteSpace(surname) && !string.IsNullOrWhiteSpace(fullName))
        {
            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            surname = parts.Length > 0 ? parts[^1] : "";
            givenName = parts.Length > 1 ? string.Join(' ', parts[..^1]) : "";
        }
        
        newUser.Profile.Name = Name.Create(givenName, surname);
        if (avatar is not null) newUser.Profile.AvatarUrl = avatar;

        await uow.Users.AddAsync(newUser);
        await uow.SaveChangesAsync();

        return newUser;
    }

    public async Task<UserDto.UserRes> UpdateProfile(Guid userId, UpdateProfileReq req, CancellationToken ct = default)
    {
        ValidationHelper.Validate(
            (userId == Guid.Empty, "Id của người dùng không được để trống."),
            (await uow.Users.FindAsync(userId) is null, "Người dùng không tồn tại.")
        );
        
        var user = await uow.Users.GetByIdWithProfileAsync(userId)
                   ?? throw new NotFoundException("Người dùng", userId.ToString());

        var profile = user.Profile;
        
        var firstName = !string.IsNullOrWhiteSpace(req.FirstName)
            ? req.FirstName!
            : profile.Name?.FirstName ?? string.Empty;
        var lastName = !string.IsNullOrWhiteSpace(req.LastName)
            ? req.LastName!
            : profile.Name?.LastName ?? string.Empty;
        profile.Name = Name.Create(firstName, lastName);
        
        if (req.DateOfBirth.HasValue)
        {
            profile.DateOfBirth = req.DateOfBirth.Value;
        }
        
        if (req.Avatar is not null && req.Avatar.Length > 0)
        {
            if (req.Avatar.Length > MaxSize)
                throw new BadHttpRequestException("Ảnh quá lớn (tối đa 5MB).");
            
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "image/jpeg", "image/png", "image/webp", "image/gif"
            };
            var contentType = req.Avatar.ContentType?.Trim() ?? "application/octet-stream";
            if (!allowed.Contains(contentType))
                throw new BadHttpRequestException("Định dạng ảnh không hợp lệ.");
            
            await using var stream = req.Avatar.OpenReadStream();

            var ext = Path.GetExtension(req.Avatar.FileName);
            var bucket = opt.Value.Buckets.Images;
            var key = $"users/{userId}/avatar-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{ext}";
            
            var put = await storage.UploadAsync(
                bucket, stream, key, contentType, ct);
            
            var asset = new ImageAsset
            {
                Kind = ImageKind.Avatar,
                Alt = "",
                Bucket = put.Bucket,
                ObjectKey = put.ObjectKey,
                Endpoint = opt.Value.S3.Endpoint,
                ContentType = contentType,
                SizeBytes = put.SizeBytes,
                ETag = put.ETag,
                CreatedAt = DateTime.UtcNow
            };
                
            await uow.ImageAssets.AddAsync(asset, ct);
        }
        
        user.Profile = profile;
        await uow.SaveChangesAsync(ct);

        return await user.ToUserResAsync(storage, ct);
    }

    public async Task UpsertHistory(Guid userId, int movieId, int? episodeId, int positionSeconds, CancellationToken ct = default)
    {
        var history = await uow.Histories.GetAsync(userId, movieId, episodeId, ct);
        var now = DateTime.UtcNow;

        if (history is null)
        {
            history = new History
            {
                UserId = userId,
                MovieId = movieId,
                EpisodeId = episodeId,
            };
            
            await uow.Histories.AddAsync(history, ct);
        }
        else
        {
            if (positionSeconds > history.PositionSecond)
                history.PositionSecond = positionSeconds;
            history.WatchedDate = now;

            uow.Histories.Update(history);
        }

        await uow.SaveChangesAsync(ct);
    }

    public async Task Delete(Guid id)
    {
        var user = await uow.Users.FindAsync(id);
        ValidationHelper.Validate(
            (id == Guid.Empty, "Id của người dùng không được để trống."),
            (user is null, "Người dùng không tồn tại.")
        );

        if (user != null) uow.Users.Remove(user);
        await uow.SaveChangesAsync();
    }
    
    private async Task<byte[]> ToByteArrayAsync(IFormFile file)
    {
        if (file.Length == 0)
            return [];

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return ms.ToArray();
    }
}