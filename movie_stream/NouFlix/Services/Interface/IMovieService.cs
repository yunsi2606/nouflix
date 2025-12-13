using NouFlix.DTOs;
using NouFlix.Models.Common;

namespace NouFlix.Services.Interface;

public interface IMovieService
{
    Task<IEnumerable<MovieRes>> GetMostViewed(int take = 12, CancellationToken ct = default);
    Task<IEnumerable<MovieDetailRes>> GetTrending(int take = 12, CancellationToken ct = default);
    Task<IEnumerable<MovieRes>> GetPopular(int take = 12, CancellationToken ct = default);
    Task<IEnumerable<MovieRes>> GetMostRating(int take = 12, CancellationToken ct = default);
    Task<IEnumerable<MovieRes>> GetNew(int take = 12, CancellationToken ct = default);
    Task<MovieDetailRes> GetById(int id, CancellationToken ct = default);
    Task<MovieDetailRes> GetBySlug(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<MovieRes>> GetSimilar(int movieId, int topK = 20, bool includeVip = false, CancellationToken ct = default);
    Task<SearchRes<IEnumerable<MovieRes>>> SearchAsync(string? q, int skip, int take, CancellationToken ct = default);
    Task<IEnumerable<MovieRes>> GetByGenreAsync(int genreId, CancellationToken ct = default);
    Task<int> CreateAsync(UpsertMovieReq req, CancellationToken ct = default);
    Task UpdateAsync(int id, UpsertMovieReq req, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
