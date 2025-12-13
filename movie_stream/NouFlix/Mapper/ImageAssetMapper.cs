using NouFlix.DTOs;
using NouFlix.Models.Entities;
using NouFlix.Services;

namespace NouFlix.Mapper;

public static class ImageAssetMapper
{
    public static async Task<AssetsDto.ImageAssetRes> ToImageAssetResAsync(
        this ImageAsset ia,
        IMinioObjectStorage storage,
        CancellationToken ct = default)
    {
        var posterUrl = (await storage.GetReadSignedUrlAsync(
                ia.Bucket, ia.ObjectKey, TimeSpan.FromMinutes(10), ct: ct)).ToString();
        
        return new AssetsDto.ImageAssetRes(ia.Id, ia.Alt, ia.Endpoint ?? "", ia.Bucket, ia.ObjectKey, posterUrl);
    }
    
    public static Task<AssetsDto.ImageAssetRes[]> ToImageAssetResListAsync(
        this IEnumerable<ImageAsset> assets,
        IMinioObjectStorage storage,
        CancellationToken ct = default)
        => Task.WhenAll(assets.Select(s => ToImageAssetResAsync(s, storage, ct)).ToArray());
}