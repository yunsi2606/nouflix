using NouFlix.DTOs;
using NouFlix.Models.Entities;
using NouFlix.Services;

namespace NouFlix.Mapper;

public static class UserMapper
{
    public static async Task<UserDto.UserRes> ToUserResAsync(
        this User u,
        IMinioObjectStorage storage,
        CancellationToken ct)
    {
        var img = u.Profile.Avatar;

        string? avatarUrl = null;
        if (img is not null) 
            avatarUrl = u.Profile.AvatarUrl ?? (await storage.GetReadSignedUrlAsync(
                img.Bucket,
                img.ObjectKey,
                TimeSpan.FromMinutes(10),
                ct: ct)).ToString();
        
        
        return new UserDto.UserRes(
            u.Id,
            u.Email.Address,
            u.Profile.Name?.FirstName ?? null,
            u.Profile.Name?.LastName ?? null,
            avatarUrl,
            Dob: u.Profile.DateOfBirth ?? null,
            MapRole(u),
            u.IsBanned,
            u.CreatedAt,
            (await u.Histories.ToItemListResAsync(ct)).ToList());
    }
    
    public static string MapRole(this User baseUser) => baseUser switch
    {
        Admin => "Admin",
        _ => "User"
    };
}