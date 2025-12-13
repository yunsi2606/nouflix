using NouFlix.DTOs;
using NouFlix.Models.Entities;
using NouFlix.Services;

namespace NouFlix.Mapper;

public static class ReviewMapper
{
    public static async Task<ReviewRes> ToReviewResAsync(
        this Review r,
        IMinioObjectStorage storage,
        CancellationToken ct = default)
    {
        var img = r.User.Profile.Avatar;

        var avatarUrl = (await storage.GetReadSignedUrlAsync(
            img.Bucket,
            img.ObjectKey,
            TimeSpan.FromMinutes(10),
            ct: ct)).ToString();

        return new ReviewRes(
            r.User.Email.ToString(),
            new UserRatingDto(
                r.User.Profile.Name!.ToString(),
                r.User.Email.ToString(),
                avatarUrl,
                r.Number),
            r.Content,
            r.CreatedAt,
            r.UpdatedAt
        );
    }
}