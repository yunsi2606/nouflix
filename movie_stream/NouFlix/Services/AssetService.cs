using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NouFlix.DTOs;
using NouFlix.Mapper;
using NouFlix.Models.Common;
using NouFlix.Models.Entities;
using NouFlix.Models.Specification;
using NouFlix.Models.ValueObject;
using NouFlix.Persistence.Repositories.Interfaces;

namespace NouFlix.Services;

public class AssetService(
    IUnitOfWork uow,
    IMinioObjectStorage storage,
    IOptions<StorageOptions> opt
    )
{
    public async Task<IEnumerable<AssetsDto.VideoAssetRes>> GetVideoByMovieId(int movId, CancellationToken ct = default)
        => await (await uow.VideoAssets.GetByMovieId(movId)).ToVideoAssetResListAsync(ct);
    
    public async Task<IEnumerable<AssetsDto.VideoAssetRes>> GetVideoByEpisodeId(int epId, CancellationToken ct = default)
        => await (await uow.VideoAssets.GetByEpisodeId(epId)).ToVideoAssetResListAsync(ct);

    public async Task<IEnumerable<AssetsDto.VideoAssetRes>> GetVideoByEpisodeIds(int[] ids, CancellationToken ct = default)
    {
        if (ids.Length == 0) return [];
        var list = await uow.VideoAssets
            .Query()
            .Where(v => v.EpisodeId != null && ids.Contains(v.EpisodeId!.Value))
            .ToListAsync(ct);

        return await list.ToVideoAssetResListAsync(ct);
    }
    
    public async Task DeleteVideoAsync(int id, CancellationToken ct = default)
    {
        if(await uow.VideoAssets.FindAsync(id) is not { } vid) 
            throw new NotFoundException("video", id);

        await storage.DeleteAsync(vid.Bucket, vid.ObjectKey, ct);
        uow.VideoAssets.Remove(vid);
        await uow.SaveChangesAsync(ct);
    }
    
    public async Task<SubtitleDto.SubtitleUploadRes> UploadRawVttAsync(
        int movieId, int? episodeId, string lang, string label, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new InvalidOperationException("File .vtt trống.");
        var ext = Path.GetExtension(file.FileName);
        if (!ext.Equals(".vtt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Vui lòng upload file đuôi .vtt");
        
        var movieExists = await uow.Movies
            .Query()
            .AnyAsync(x => x.Id == movieId, ct);;
        if (!movieExists ) throw new InvalidOperationException("Không tìm thấy phim.");

        string prefix;

        if (episodeId is { } eid)
        {
            var exists = await uow.Episodes
                .Query()
                .AnyAsync(x => x.Id == eid, ct);
            if (!exists) throw new InvalidOperationException("Không tìm thấy tập.");

            prefix = $"hls/movies/{movieId}/episodes/{eid}/subs/{Slugify(lang)}";
        }
        else
        {
            prefix = $"hls/movies/{movieId}/subs/{Slugify(lang)}";
        }

        var bucket = opt.Value.Buckets.Videos; // hoặc Buckets.Subtitles nếu bạn có
        var objectKey = $"{prefix}/{DateTime.UtcNow.Ticks}.vtt";

        await using var stream = file.OpenReadStream();
        await storage.UploadAsync(bucket, stream, objectKey, "text/vtt", ct);

        var entity = new SubtitleAsset
        {
            MovieId = movieId,
            EpisodeId = episodeId,
            Language = lang,
            Label = label,
            Bucket = bucket,
            ObjectKey = objectKey,
            Endpoint = opt.Value.S3.Endpoint
        };
        await uow.SubtitleAssets.AddAsync(entity, ct);
        await uow.SaveChangesAsync(ct);

        return new SubtitleDto.SubtitleUploadRes(
            Id: entity.Id,
            MovieId: movieId,
            EpisodeId: episodeId,
            Language: lang,
            Label: label,
            Bucket: bucket,
            ObjectKey: objectKey,
            Endpoint: entity.Endpoint,
            PublicUrl: BuildPublicUrl(entity.Endpoint, bucket, objectKey));
    }

    public async Task<IEnumerable<SubtitleDto.SubtitleUploadRes>> GetSubtitlesByMovieAsync(int movieId, CancellationToken ct = default)
    {
        var list = await uow.SubtitleAssets
            .Query()
            .Where(x => x.MovieId == movieId && x.EpisodeId == null)
            .ToListAsync(ct);

        return list.Select(x => new SubtitleDto.SubtitleUploadRes(
            Id: x.Id,
            MovieId: x.MovieId ?? 0,
            EpisodeId: x.EpisodeId,
            Language: x.Language,
            Label: x.Label,
            Bucket: x.Bucket,
            ObjectKey: x.ObjectKey,
            Endpoint: x.Endpoint,
            PublicUrl: BuildPublicUrl(x.Endpoint, x.Bucket, x.ObjectKey)
        ));
    }

    public async Task<IEnumerable<SubtitleDto.SubtitleUploadRes>> GetSubtitlesByEpisodeAsync(int episodeId, CancellationToken ct = default)
    {
        var list = await uow.SubtitleAssets
            .Query()
            .Where(x => x.EpisodeId == episodeId)
            .ToListAsync(ct);

        return list.Select(x => new SubtitleDto.SubtitleUploadRes(
            Id: x.Id,
            MovieId: x.MovieId ?? 0,
            EpisodeId: x.EpisodeId,
            Language: x.Language,
            Label: x.Label,
            Bucket: x.Bucket,
            ObjectKey: x.ObjectKey,
            Endpoint: x.Endpoint,
            PublicUrl: BuildPublicUrl(x.Endpoint, x.Bucket, x.ObjectKey)
        ));
    }

    public async Task DeleteSubtitleAsync(int id, CancellationToken ct = default)
    {
        if (await uow.SubtitleAssets.FindAsync(id) is not { } sub)
            throw new NotFoundException("subtitle", id);

        await storage.DeleteAsync(sub.Bucket, sub.ObjectKey, ct);
        uow.SubtitleAssets.Remove(sub);
        await uow.SaveChangesAsync(ct);
    }

    public async Task<IEnumerable<AssetsDto.ImageAssetRes>> GetImageByKind(int movieId, ImageKind kind,
        CancellationToken ct = default)
        => await (await uow.ImageAssets.GetByKind(movieId, kind)).ToImageAssetResListAsync(storage, ct);

    public async Task<AssetsDto.ImageAssetRes> GetPosterAsync(int movieId, CancellationToken ct = default)
    {
        var image = await uow.ImageAssets
            .Query()
            .FirstOrDefaultAsync(x => x.MovieId == movieId && x.Kind == ImageKind.Poster, ct);
        
        if (image is null) throw new WarningException("Poster not found");

        return await image.ToImageAssetResAsync(storage, ct);
    }
    
    public async Task<Uri> GetPreviewAsync(string bucket, string objectKey, CancellationToken ct = default)
        => await storage.GetReadSignedUrlAsync(bucket, objectKey, TimeSpan.FromMinutes(10), 0, ct);

    public async Task CreateImageAsync(ImageAsset image, CancellationToken ct = default)
    {
        await uow.ImageAssets.AddAsync(image, ct);
        await uow.SaveChangesAsync(ct);
    }

    public async Task DeleteImageAsync(int id, CancellationToken ct = default)
    {
        if(await uow.ImageAssets.FindAsync(id) is not { } img) 
            throw new NotFoundException("image assets", id);
        
        await storage.DeleteAsync(img.Bucket, img.ObjectKey, ct);
        uow.ImageAssets.Remove(img);
        await uow.SaveChangesAsync(ct);
    }

    private static string BuildPublicUrl(string? endpoint, string bucket, string objectKey)
    {
        var ep = (endpoint ?? "").Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(ep)) return $"/{bucket}/{objectKey}";
        if (!ep.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !ep.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            ep = "http://" + ep.TrimStart('/');
        }
        return $"{ep}/{bucket}/{objectKey}";
    }
    
    private static string Slugify(string s)
        => string.Concat((s ?? "").ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-'));
}