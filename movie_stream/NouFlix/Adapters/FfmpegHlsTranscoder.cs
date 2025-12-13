using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NouFlix.DTOs;
using NouFlix.Models.Entities;
using NouFlix.Models.Specification;
using NouFlix.Models.ValueObject;
using NouFlix.Persistence.Data;
using NouFlix.Persistence.Repositories.Interfaces;
using NouFlix.Services;
using NouFlix.Services.Interface;

namespace NouFlix.Adapters;

public class FfmpegHlsTranscoder(
    IMinioObjectStorage storage,
    IStatusStorage<TranscodeDto.TranscodeStatus> transStatus,
    IStatusStorage<SubtitleDto.SubtitleStatus> subStatus,
    IUnitOfWork uow,
    IOptions<StorageOptions> opt,
    IOptions<FfmpegOptions> ffOpts)
{
    private static string Posix(string path) => path.Replace("\\", "/");

    private string ResolveFfmpegPath()
    {
        // Ưu tiên: cấu hình -> ENV -> "ffmpeg"
        var p = ffOpts.Value.Path
                ?? Environment.GetEnvironmentVariable("FFMPEG_PATH")
                ?? "ffmpeg";

        // Windows: nếu path tuyệt đối mà chưa có .exe
        if (OperatingSystem.IsWindows()
            && Path.IsPathRooted(p)
            && string.IsNullOrWhiteSpace(Path.GetExtension(p)))
        {
            p += ".exe";
        }

        if (Path.IsPathRooted(p) && !File.Exists(p))
            throw new FileNotFoundException($"Không tìm thấy ffmpeg: {p}");

        return p;
    }

    private string ResolveFfprobePath()
    {
        // ffprobe cùng thư mục với ffmpeg (hoặc đặt ENV FFMPEG_PATH chuẩn)
        var ffmpegPath = ResolveFfmpegPath();
        var dir = Path.GetDirectoryName(ffmpegPath)!;
        var probe = Path.Combine(dir, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        if (!File.Exists(probe))
            throw new FileNotFoundException($"Không tìm thấy ffprobe: {probe}");
        return probe;
    }

    private async Task<bool> HasAudioAsync(string ffprobePath, string inputPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffprobePath,
            Arguments = $"-v error -select_streams a -show_entries stream=index -of csv=p=0 \"{inputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        var stdout = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return !string.IsNullOrWhiteSpace(stdout);
    }
    
    private static string VideoBitrate(string profile) => profile switch
    {
        "1080" => "5000k",
        "720" => "2800k",
        _ => "1400k"
    };
    
    private static TimeSpan? ParseDurationFromProbeJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var durStr = doc.RootElement.TryGetProperty("format", out var f) &&
                     f.TryGetProperty("duration", out var d) ? d.GetString() : null;
        return double.TryParse(durStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var sec)
            ? TimeSpan.FromSeconds(sec) : null;
    }

    public async Task<(VideoAsset Master, List<VideoAsset> Variants)> RunTranscodeAsync(
        TranscodeDto.TranscodeJob job, CancellationToken ct)
    {
        var ffmpegPath = ResolveFfmpegPath();
        var ffprobePath = ResolveFfprobePath();

        // Cập nhật trạng thái
        transStatus.Upsert(new TranscodeDto.TranscodeStatus
        {
            JobId = job.JobId,
            State = "Running",
            Progress = 0
        });

        var tmpRoot = Path.Combine(Path.GetTempPath(), "hls_work", job.JobId);
        var outRoot = Path.Combine(tmpRoot, "out");
        
        Directory.CreateDirectory(outRoot);

        // Tải video nguồn về /tmp
        var inputPath = Path.Combine(tmpRoot, Path.GetFileName(job.SourceKey));
        await using (var fs = File.Create(inputPath))
            await storage.DownloadAsync(job.SourceBucket, job.SourceKey, fs, ct);

        // Lấy duration để tính % (optional)
        var dur = await GetDurationAsync(ffprobePath, inputPath, ct);
        var total = dur ?? TimeSpan.Zero;
        
        if (dur is {} duration)
        {
            if (!job.EpisodeId.HasValue)
            {
                var movie = await uow.Movies.FindAsync(job.MovieId, ct);
                if (movie is not null && movie.Type == MovieType.Single)
                {
                    movie.Runtime = duration;
                    
                    uow.Movies.Update(movie);
                    await uow.SaveChangesAsync(ct);
                }
            }
            else
            {
                var episodeId = job.EpisodeId.Value;
                var ep = await uow.Episodes.FindAsync(episodeId, ct);
                if (ep is not null)
                {
                    if (ep.Duration != duration)
                    {
                        ep.Duration = duration;
                        await uow.SaveChangesAsync(ct);
                    }
                }
            }
        }

        // Lập filter/args (từ code của bạn)
        // ... giữ nguyên build filter/map/vmap/encV/segPattern/outPattern ...
        var ps = (job.Profiles?.Length > 0 ? job.Profiles : ["1080","720","480"])
                 .OrderByDescending(p => int.TryParse(p, out var n) ? n : 0)
                 .ToArray();

        // hasAudio & filter giống code bạn
        var hasAudio = await HasAudioAsync(ffprobePath, inputPath, ct);
        
        var splitLabels = string.Concat(ps.Select((_, i) => $"[v{i}]"));
        
        var filter = 
            $"[0:v]split={ps.Length}{splitLabels};" + 
            string.Join(';', ps.Select((p, i) => $"[v{i}]scale=-2:{p}[vo{i}]"));

        var mapPairs = string.Join(
            ' ',
            Enumerable.Range(0, ps.Length).Select(i =>
                hasAudio
                    ? $"-map [vo{i}] -map 0:a:0"
                    : $"-map [vo{i}]"
            )
        );
        
        var vmap = string.Join(
            ' ',
            Enumerable.Range(0, ps.Length).Select(i =>
                hasAudio
                    ? $"v:{i},a:{i}"
                    : $"v:{i}"
            )
        );
        
        var encV = string.Join(
            ' ',
            ps.Select((p, i) =>
                $"-c:v:{i} libx264 -preset veryfast -g 48 -keyint_min 48 -b:v:{i} {VideoBitrate(p)}"
            )
        );

        var segPattern = Posix(Path.Combine(outRoot, "%v", "seg_%06d.ts"));
        var outPattern = Posix(Path.Combine(outRoot, "%v", "index.m3u8"));
        const string masterName = "master.m3u8";

        var args =
            $"-y -i \"{inputPath}\" " +
            $"-filter_complex \"{filter}\" {mapPairs} " +
            "-c:a aac -ac 2 -ar 48000 -b:a 128k " +
            $"{encV} " +
            "-f hls " +
            "-hls_time 6 " +
            "-hls_playlist_type vod " +
            "-hls_flags independent_segments " +
            $"-hls_segment_filename \"{segPattern}\" " +
            $"-master_pl_name \"{masterName}\" " +
            $"-var_stream_map \"{vmap}\" " +
            $"\"{outPattern}\"";

        // Chạy ffmpeg và cập nhật progress theo stderr time=
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args,
            WorkingDirectory = tmpRoot,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (var p = Process.Start(psi)!)
        {
            // đọc từng dòng stderr → tìm "time=00:01:23.45"
            while (!p.HasExited)
            {
                var line = await p.StandardError.ReadLineAsync(ct);
                if (line is null) break;
                var t = ExtractTime(line);
                if (t is not null && total > TimeSpan.Zero)
                {
                    var pct = (int)Math.Clamp(
                        t.Value.TotalMilliseconds / total.TotalMilliseconds * 100,
                        0,
                        99
                    );

                    transStatus.Upsert(new TranscodeDto.TranscodeStatus
                    {
                        JobId = job.JobId,
                        State = "Running",
                        Progress = pct
                    });
                }
            }
            await p.WaitForExitAsync(ct);
            if (p.ExitCode != 0)
                throw new InvalidOperationException("ffmpeg failed");
        }

        // Upload HLS lên MinIO & ghi DB (giữ y chang code bạn)
        var bucket = opt.Value.Buckets.Videos ?? "videos";
        var basePrefix = job.EpisodeId is null
            ? $"hls/movies/{job.MovieId}"
            : $"hls/movies/{job.MovieId}/ss{job.SeasonNumber}/ep{job.EpisodeNumber}";

        var masterKey = $"{basePrefix}/master.m3u8";
        await using (var ms = File.OpenRead(Path.Combine(outRoot, masterName))) 
            await storage.UploadAsync(bucket, ms, masterKey, "application/vnd.apple.mpegurl", ct);

        for (int i = 0; i < ps.Length; i++)
        {
            var profile = ps[i];
            var localDir = Directory.Exists(Path.Combine(outRoot, i.ToString()))
                ? Path.Combine(outRoot, i.ToString())
                : Path.Combine(outRoot, profile);

            var localIndex = Path.Combine(localDir, "index.m3u8");
            await using (var ims = File.OpenRead(localIndex))
                await storage.UploadAsync(bucket, ims, $"{basePrefix}/{profile}/index.m3u8", "application/vnd.apple.mpegurl", ct);

            foreach (var segFile in Directory.EnumerateFiles(localDir, "seg_*.ts"))
            {
                var segName = Path.GetFileName(segFile);
                await using var sfs = File.OpenRead(segFile);
                await storage.UploadAsync(bucket, sfs, $"{basePrefix}/{profile}/{segName}", "video/mp2t", ct);
            }
        }

        var created = new List<VideoAsset>();
        var master = new VideoAsset
        {
            MovieId = job.MovieId,
            EpisodeId = job.EpisodeId,
            Kind = VideoKind.Master,
            Quality = QualityLevel.High,
            Language = job.Language,
            Bucket = bucket,
            ObjectKey = masterKey,
            Endpoint = opt.Value.S3.Endpoint,
            ContentType = "application/vnd.apple.mpegurl",
            Status = PublishStatus.Published
        };
        created.Add(master);

        foreach (var pQual in ps)
        {
            created.Add(new VideoAsset
            {
                MovieId = job.MovieId,
                EpisodeId = job.EpisodeId,
                Kind = VideoKind.Variant,
                Quality = Enum.TryParse<QualityLevel>(pQual, true, out var q) ? q : QualityLevel.Medium,
                Language = job.Language,
                Bucket = bucket,
                ObjectKey = $"{basePrefix}/{pQual}/index.m3u8",
                Endpoint = opt.Value.S3.Endpoint,
                ContentType = "application/vnd.apple.mpegurl",
                Status = PublishStatus.Published
            });
        }

        await uow.VideoAssets.AddRangeAsync(created, ct);
        await uow.SaveChangesAsync(ct);

        try
        {
            Directory.Delete(tmpRoot, true);
        }
        catch
        {
            // ignored
        }

        transStatus.Upsert(new TranscodeDto.TranscodeStatus
        {
            JobId = job.JobId,
            State = "Done",
            Progress = 100,
            MasterKey = masterKey
        });
        
        return (master, created.Where(x => x.Kind == VideoKind.Variant).ToList());

        // helpers
        static TimeSpan? ExtractTime(string line)
        {
            // match: time=00:01:23.45
            var idx = line.IndexOf("time=", StringComparison.Ordinal);
            if (idx < 0) return null;
            
            var s = line.Substring(idx + 5).Trim();
            
            var space = s.IndexOf(' ');
            if (space > 0) s = s[..space];
            
            return TimeSpan.TryParseExact(s, @"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture, out var ts)
                ? ts : null;
        }
    }
    
    // Upload VTT "raw" (không segment). Không sửa master.
    public async Task<SubtitleAsset> RunSubtitleAsync(
    SubtitleDto.SubtitleJob job,
    Stream stream,
    CancellationToken ct = default)
    {
        subStatus.Upsert(new SubtitleDto.SubtitleStatus
        {
            JobId = job.JobId,
            State = "Running",
            Progress = 10
        });
    
        // Validate
        if (string.IsNullOrWhiteSpace(job.DestBucket) || string.IsNullOrWhiteSpace(job.DestKey))
            throw new InvalidOperationException("Thiếu SourceBucket/SourceKey cho VTT nguồn.");
        if (!Path.GetExtension(job.DestKey).Equals(".vtt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Subtitle phải là .vtt (nếu .srt, hãy convert trước).");
    
        // key đích
        var bucket = job.DestBucket;
        var key = job.DestKey;
        
        await storage.UploadAsync(bucket, stream, key, "text/vtt", ct);
        
        var sub = new SubtitleAsset
        {
            MovieId = job.MovieId,
            EpisodeId = job.EpisodeId,
            Language = job.Language,
            Label = job.Label,
            Bucket = bucket,
            ObjectKey = key,
            Endpoint = opt.Value.S3.Endpoint,
        };
        await uow.SubtitleAssets.AddAsync(sub, ct);
        await uow.SaveChangesAsync(ct);
        
        subStatus.Upsert(new SubtitleDto.SubtitleStatus
        {
            JobId = job.JobId,
            State = "Done",
            Progress = 100,
            IndexKey = key
        });
    
        return sub;
    }

    private async Task<TimeSpan?> GetDurationAsync(string ffprobe, string input, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffprobe,
            Arguments = $"-v quiet -print_format json -show_format \"{input}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi)!;
        var json = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return ParseDurationFromProbeJson(json);
    }
}