using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NouFlix.DTOs;
using NouFlix.Models.Common;
using NouFlix.Models.Specification;
using NouFlix.Services;
using NouFlix.Services.Interface;

namespace NouFlix.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SubtitleController(
    IMinioObjectStorage storage,
    IQueue<SubtitleDto.SubtitleJob> queue,
    IStatusStorage<SubtitleDto.SubtitleStatus> status,
    IOptions<StorageOptions> opt,
    AssetService svc) : Controller
{
    [HttpPost("upload-raw")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UploadRawVtt(
        [FromForm] int movieId,
        [FromForm] int? episodeId,
        [FromForm] int? episodeNumber,
        [FromForm] string? language,
        [FromForm] string? label,
        IFormFile file,
        CancellationToken ct)
    {
        if (!Path.GetExtension(file.FileName).Equals(".vtt", StringComparison.OrdinalIgnoreCase))
            return BadRequest("File must be .vtt");

        var jobId = Guid.NewGuid().ToString("N");
        var lang = string.IsNullOrWhiteSpace(language) ? "vi" : language!;
        var lbl = string.IsNullOrWhiteSpace(label) ? "Tiếng Việt" : label!;
        
        var tempBucket = opt.Value.Buckets.Temps ?? "temps";
        var tempKey = $"sub/uploads/{movieId}/{(episodeId is null ? "single" : $"ep{episodeNumber}")}/{jobId}/{file.FileName}";
        await using (var s = file.OpenReadStream())
            await storage.UploadAsync(tempBucket, s, tempKey, "text/vtt; charset=utf-8", ct);
        
        var presignedUrl = await storage.GetReadSignedUrlAsync(tempBucket, tempKey, TimeSpan.FromMinutes(10), ct: ct);
        
        var destBucket = opt.Value.Buckets.Videos ?? "videos";
        var destKey = episodeId is null
            ? $"hls/movies/{movieId}/sub/{lang}/index.vtt"
            : $"hls/movies/{movieId}/ep{episodeNumber}/sub/{lang}/index.vtt";
        
        var job = new SubtitleDto.SubtitleJob
        {
            JobId = jobId,
            MovieId = movieId,
            EpisodeId = episodeId,
            EpisodeNumber = episodeNumber,
            Language = lang,
            Label = lbl,
            PresignedUrl = presignedUrl.ToString(),
            DestBucket = destBucket,
            DestKey = destKey
        };

        status.Upsert(new SubtitleDto.SubtitleStatus { JobId = jobId, State = "Queued", Progress = 0 });
        await queue.EnqueueAsync(job, ct);
        
        return Accepted(new { jobId });
    }

    [HttpGet("{jobId}/status")]
    [Authorize(Roles = "Admin")]
    public IActionResult GetStatus(string jobId, [FromServices] IStatusStorage<SubtitleDto.SubtitleStatus> store)
        => store.Get(jobId) is { } s ? Ok(s) : NotFound();
    
    [HttpPost("raw")]
    [Authorize(Roles = "Admin")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadRaw(
        [FromRoute] int movieId,
        [FromQuery] int? episodeId,
        [FromForm] SubtitleDto.UploadReq req,
        CancellationToken ct)
    {
        var res = await svc.UploadRawVttAsync(movieId, episodeId, req.Lang, req.Label, req.File, ct);
        return Ok(GlobalResponse<SubtitleDto.SubtitleUploadRes>.Success(res));
    }
    
    [HttpGet("movie/{movieId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetByMovie(int movieId, CancellationToken ct)
        => Ok(GlobalResponse<IEnumerable<SubtitleDto.SubtitleUploadRes>>.Success(await svc.GetSubtitlesByMovieAsync(movieId, ct)));

    [HttpGet("episode/{episodeId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetByEpisode(int episodeId, CancellationToken ct)
        => Ok(GlobalResponse<IEnumerable<SubtitleDto.SubtitleUploadRes>>.Success(await svc.GetSubtitlesByEpisodeAsync(episodeId, ct)));

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await svc.DeleteSubtitleAsync(id, ct);
        return Ok(GlobalResponse<object>.Success(null));
    }
}