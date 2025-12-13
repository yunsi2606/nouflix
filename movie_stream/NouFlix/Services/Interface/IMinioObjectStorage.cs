using NouFlix.DTOs;

namespace NouFlix.Services;

public interface IMinioObjectStorage
{
    Task<Uri> GetReadSignedUrlAsync(string bucket, string objectKey, TimeSpan ttl, int audience = 1, CancellationToken ct = default);
    Task<Uri> GetWriteSignedUrlAsync(string bucket, string objectKey, TimeSpan? ttl = null, CancellationToken ct = default);
    Task<UploadResult> UploadAsync(string bucket, Stream stream, string objectName, string? contentType, CancellationToken ct = default);
    Task DeleteAsync(string bucket, string objectKey, CancellationToken ct = default);
    Task<string> DownloadTextAsync(string bucket, string objectKey, CancellationToken ct = default);
    Task UploadTextAsync(string bucket, string objectKey, string text, string contentType, CancellationToken ct = default);
    Task DownloadAsync(string bucket, string objectKey, Stream destination, CancellationToken ct = default);
    Task<(bool Ok, string? Error)> CheckBucketAsync(string bucket, bool createIfMissing = false, CancellationToken ct = default);
}
