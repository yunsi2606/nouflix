using NouFlix.DTOs;
using NouFlix.Models.Entities;
using NouFlix.Models.ValueObject;
using NouFlix.Services;

namespace NouFlix.Mapper;

public static class MovieMapper
{
    public static async Task<MovieRes> ToMovieResAsync(
        this Movie m,
        IMinioObjectStorage storage,
        CancellationToken ct = default)
    {
        var img = m.Images?.OrderBy(i => i.Id).FirstOrDefault(i => i.Kind == ImageKind.Poster);

        var posterUrl = img is null
            ? string.Empty
            : (await storage.GetReadSignedUrlAsync(
                img.Bucket, img.ObjectKey, TimeSpan.FromMinutes(10), ct: ct)).ToString();

        var genres = m.MovieGenres?
            .Select(mg => new GenreDto.GenreRes(mg.Genre.Id, mg.Genre.Name, mg.Genre.Icon, m.MovieGenres.Count))
            .ToList() ?? new List<GenreDto.GenreRes>();

        return new MovieRes(
            m.Id,
            m.Slug,
            m.Title,
            posterUrl,
            m.Type.ToString(),
            m.Status.ToString(),
            m.AvgRating,
            m.ViewCount,
            m.ReleaseDate,
            genres
        );
    }
    
    public static async Task<MovieDetailRes> ToMovieDetailAsync(
        this Movie m,
        MinioObjectStorage storage,
        CancellationToken ct = default)
    {
        var poster = m.Images
            .OrderBy(i => i.Id)
            .FirstOrDefault(i => i.Kind == ImageKind.Poster);
        
        var backdrop = m.Images
            .OrderBy(i => i.Id)
            .FirstOrDefault(i => i.Kind == ImageKind.Backdrop);

        var posterUrl = poster is null
            ? string.Empty
            : (await storage.GetReadSignedUrlAsync(
                poster.Bucket, poster.ObjectKey, TimeSpan.FromMinutes(10), ct: ct)).ToString();
        
        var backdropUrl = backdrop is null
            ? string.Empty
            : (await storage.GetReadSignedUrlAsync(
                backdrop.Bucket, backdrop.ObjectKey, TimeSpan.FromMinutes(10), ct: ct)).ToString();

        var genres = m.MovieGenres
            .Select(mg => new GenreDto.GenreRes(mg.Genre.Id, mg.Genre.Name, mg.Genre.Icon, m.MovieGenres.Count))
            .ToList();
        
        var studios = m.MovieStudios
            .Select(mg => new StudioRes(mg.Studio.Id, mg.Studio.Name))
            .ToList();

        return new MovieDetailRes(
            m.Id,
            m.Slug,
            m.Title,
            m.AlternateTitle,
            m.Synopsis,
            posterUrl,
            backdropUrl,
            m.ReleaseDate,
            m.Director,
            m.TotalDurationMinutes,
            m.AvgRating,
            m.AgeRating,
            m.VoteCount,
            m.ViewCount,
            genres,
            studios,
            m.Country,
            m.Language,
            m.Status,
            m.Type,
            m.Quality,
            m.HasVideo,
            m.IsVipOnly,
            m.CreatedAt,
            m.UpdatedAt
        );
    }

    public static Task<MovieRes[]> ToMovieResListAsync(
        this IEnumerable<Movie> movies,
        MinioObjectStorage storage,
        CancellationToken ct = default)
        => Task.WhenAll(movies.Select(m => m.ToMovieResAsync(storage, ct)));
    
    public static Task<MovieDetailRes[]> ToMovieDetailListAsync(
        this IEnumerable<Movie> movies,
        MinioObjectStorage storage,
        CancellationToken ct = default)
        => Task.WhenAll(movies.Select(m => m.ToMovieDetailAsync(storage, ct)));
}