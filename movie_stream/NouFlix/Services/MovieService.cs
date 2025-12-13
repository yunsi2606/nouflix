using System.Collections;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using NouFlix.DTOs;
using NouFlix.Mapper;
using NouFlix.Models.Common;
using NouFlix.Models.Entities;
using NouFlix.Models.ValueObject;
using NouFlix.Persistence.Repositories.Interfaces;
using NouFlix.Services.Interface;
using Serilog;

namespace NouFlix.Services;

public class MovieService(
    IUnitOfWork uow,
    MinioObjectStorage storage,
    IAppCache cache,
    IHttpContextAccessor accessor) : IMovieService
{
    private readonly MovieSimWeights _w = new();
    
    private readonly Serilog.ILogger _logger = Log.ForContext<StreamService>();
    private HttpContext? HttpContext => accessor.HttpContext;

    private string? ClientIp => HttpContext?.Connection.RemoteIpAddress?.ToString();
    private string? UserAgent => HttpContext?.Request.Headers["User-Agent"].ToString();
    
    public async Task<IEnumerable<MovieRes>> GetMostViewed(int take = 12, CancellationToken ct = default)
        => await (await uow.Movies.TopByViewsAsync(take, ct)).ToMovieResListAsync(storage, ct);
    
    public async Task<IEnumerable<MovieDetailRes>> GetTrending(int take = 12, CancellationToken ct = default)
        => await (await uow.Movies.TopByViewsAsync(take, ct)).ToMovieDetailListAsync(storage, ct);
    
    public async Task<IEnumerable<MovieRes>> GetPopular(int take = 12, CancellationToken ct = default)
        => await (await uow.Movies.TopByViewsAsync(take, ct)).ToMovieResListAsync(storage, ct);
    
    public async Task<IEnumerable<MovieRes>> GetMostRating(int take = 12, CancellationToken ct = default)
        => await (await uow.Movies.TopByViewsAsync(take, ct)).ToMovieResListAsync(storage, ct);
    
    public async Task<IEnumerable<MovieRes>> GetNew(int take = 12, CancellationToken ct = default)
        => await (await uow.Movies.TopByViewsAsync(take, ct)).ToMovieResListAsync(storage, ct);

    public async Task<MovieDetailRes> GetById(int id, CancellationToken ct = default)
    {
        var mov = await uow.Movies.FindAsync(id, ct);
        if (mov is null)
            throw new NotFoundException("movie", id);
        
        return await mov.ToMovieDetailAsync(storage, ct);
    }

    public async Task<MovieDetailRes> GetBySlug(string slug, CancellationToken ct = default)
    {
        var mov = await uow.Movies.GetBySlugAsync(slug);
        if (mov is null)
            throw new NotFoundException("movie", slug);
        
        var userId = HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = HttpContext?.User?.FindFirstValue(ClaimTypes.Email)
                    ?? HttpContext?.User?.Identity?.Name;
        
        var audit = new SystemDto.AuditLog
        {
            Id = Guid.NewGuid().ToString(),
            CorrelationId = (string?)HttpContext?.Request.Headers["X-Correlation-Id"] ?? HttpContext?.TraceIdentifier,
            UserId = userId,
            Username = email,
            Action = "get",
            ResourceType = "Movie",
            ResourceId = mov.Id.ToString(),
            Details = "GetDetailMovie",
            ClientIp = ClientIp,
            UserAgent = UserAgent,
            Route = HttpContext?.Request.Path.ToString(),
            HttpMethod = HttpContext?.Request.Method,
            StatusCode = 200,
        };

        _logger.Information("Movie audit {@Audit}", audit);
        
        return await mov.ToMovieDetailAsync(storage, ct);
    }

    public async Task<IReadOnlyList<MovieRes>> GetSimilar(int movieId, int topK = 20, bool includeVip = false, CancellationToken ct = default)
    {
        var seed = await uow.Movies.FindAsync(movieId, ct) 
                   ?? throw new KeyNotFoundException();
        var cacheKey = SimilarKey(seed.Id, seed.UpdatedAt, topK, includeVip);
        
        return await cache.GetOrCreateAsync(
            cacheKey,
            async _ =>
            {
                var candidates = await uow.Movies.FindCandidatesAsync(seed, max: topK * 12, includeVip, ct);
                
                var seedGenres = seed.MovieGenres
                    .Select(g => g.GenreId)
                    .ToHashSet();
                
                var seedStudios = seed.MovieStudios
                    .Select(s => s.StudioId)
                    .ToHashSet();
                
                int? seedYear = seed.ReleaseDate?.Year;

                double Score(Movie cand)
                {
                    double sDir = (!string.IsNullOrWhiteSpace(seed.Director) && seed.Director == cand.Director) ? _w.Director : 0;
                    double sGen = _w.Genre  * Jaccard(seedGenres,  cand.MovieGenres.Select(g => g.GenreId));
                    double sStu = _w.Studio * Jaccard(seedStudios, cand.MovieStudios.Select(s => s.StudioId));
                    double sYear = (seedYear is null || cand.ReleaseDate is null) ? 0 :
                                   _w.Year * YearSimilarity(seedYear.Value, cand.ReleaseDate.Value.Year);
                    double sLang = (!string.IsNullOrWhiteSpace(seed.Language) && seed.Language == cand.Language) ? _w.Language : 0;
                    double sCty = (!string.IsNullOrWhiteSpace(seed.Country)  && seed.Country  == cand.Country)  ? _w.Country  : 0;
                    return sDir + sGen + sStu + sYear + sLang + sCty;
                }

                var ranked = candidates
                    .Where(c => c.Id != seed.Id)
                    .Select(c => new {
                        Movie = c,
                        Score = Score(c),
                        Tie = (c.ViewCount, c.Followers, c.Rating * Math.Log(1 + Math.Max(1, c.TotalRatings)))
                    })
                    .Where(x => x.Score >= 0.5)
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.Tie.ViewCount)
                    .ThenByDescending(x => x.Tie.Followers)
                    .ThenByDescending(x => x.Tie.Item3)
                    .Take(6)
                    .Select(x => x.Movie.ToMovieResAsync(storage, ct))
                    .ToList();

                var dtoArray = await Task.WhenAll(ranked);
                var dtoList = dtoArray.ToList(); // List<MovieRes> implements IReadOnlyList<MovieRes>
                return (IReadOnlyList<MovieRes>)dtoList;
            },
            ttl: TimeSpan.FromHours(6),
            ct);
    }
    
    public async Task<SearchRes<IEnumerable<MovieRes>>> SearchAsync(
        string? q, int skip, int take, CancellationToken ct = default)
    {
        // bảo vệ tham số
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 200);

        var total = await uow.Movies.CountAsync(q, skip, take, ct);
        var items = await uow.Movies.SearchAsync(q, skip, take, ct);

        return new SearchRes<IEnumerable<MovieRes>>(
            Count: total,
            Data: (await items.ToMovieResListAsync(storage, ct)).ToList()
            );
    }

    public async Task<IEnumerable<MovieRes>> GetByGenreAsync(int genreId, CancellationToken ct = default)
        => await (await uow.Movies.GetByGenreAsync(genreId, ct)).ToMovieResListAsync(storage, ct);

    public async Task<int> CreateAsync(UpsertMovieReq req, CancellationToken ct = default)
    {
        var newMov = new Movie
        {
            Title = req.Title,
            AlternateTitle = req.AlternateTitle,
            Slug = req.Slug,
            Synopsis = req.Synopsis,
            AgeRating = req.AgeRating,
            ReleaseDate = req.ReleaseDate,
            Director = req.Director,
            Country = req.Country,
            Language = req.Language,
            Type = req.Type,
            Quality = req.Quality,
            Status = req.Status,
            IsVipOnly = req.IsVipOnly
        };
        
        await uow.Movies.AddAsync(newMov, ct);
        await uow.SaveChangesAsync(ct);
        
        var userId = HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = HttpContext?.User.FindFirstValue(ClaimTypes.Email)
                    ?? HttpContext?.User.Identity?.Name;
        
        var audit = new SystemDto.AuditLog
        {
            Id = Guid.NewGuid().ToString(),
            CorrelationId = (string?)HttpContext?.Request.Headers["X-Correlation-Id"] ?? HttpContext?.TraceIdentifier,
            UserId = userId,
            Username = email,
            Action = "create",
            ResourceType = "Movie",
            ResourceId = newMov.Id.ToString(),
            Details = "CreateMovie",
            ClientIp = ClientIp,
            UserAgent = UserAgent,
            Route = HttpContext?.Request.Path.ToString(),
            HttpMethod = HttpContext?.Request.Method,
            StatusCode = StatusCodes.Status201Created,
        };

        _logger.Information("Movie audit {@Audit}", audit);

        return newMov.Id;
    }
    
    public async Task UpdateAsync(int id, UpsertMovieReq req, CancellationToken ct = default)
    {
        var userId = HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = HttpContext?.User.FindFirstValue(ClaimTypes.Email)
                    ?? HttpContext?.User.Identity?.Name;
        SystemDto.AuditLog? audit;
        
        var mov = await uow.Movies.FindAsync(id, ct);
        if (mov is null)
        {
            audit = new SystemDto.AuditLog
            {
                Id = Guid.NewGuid().ToString(),
                CorrelationId = (string?)HttpContext?.Request.Headers["X-Correlation-Id"] ?? HttpContext?.TraceIdentifier,
                UserId = userId,
                Username = email,
                Action = "update",
                ResourceType = "Movie",
                ResourceId = null,
                Details = "MovieNotFound",
                ClientIp = ClientIp,
                UserAgent = UserAgent,
                Route = HttpContext?.Request.Path.ToString(),
                HttpMethod = HttpContext?.Request.Method,
                StatusCode = StatusCodes.Status404NotFound,
            };

            _logger.Information("Movie audit {@Audit}", audit);
            
            throw new NotFoundException("movie", id);
        }
        
        mov.Title = req.Title;
        mov.AlternateTitle = req.AlternateTitle;
        mov.Slug = req.Slug;
        mov.Synopsis = req.Synopsis;
        mov.AgeRating = req.AgeRating;
        mov.ReleaseDate = req.ReleaseDate;
        mov.Director = req.Director;
        mov.Country = req.Country;
        mov.Language = req.Language;
        mov.Type = req.Type;
        mov.Quality = req.Quality;
        mov.Status = req.Status;
        mov.IsVipOnly = req.IsVipOnly;

        var newGenres = req.GenreIds.ToHashSet();
        var oldGenres = mov.MovieGenres.Select(x => x.GenreId).ToHashSet();
        var toAddG = newGenres.Except(oldGenres);
        var toRemoveG = oldGenres.Except(newGenres);
        
        foreach (var gid in toAddG)
            mov.MovieGenres.Add(new MovieGenre { MovieId = mov.Id, GenreId = gid });
        mov.MovieGenres = mov.MovieGenres.Where(x => !toRemoveG.Contains(x.GenreId)).ToList();
        
        var newStudios = req.StudioIds.ToHashSet();
        var oldStudios = mov.MovieStudios.Select(x => x.StudioId).ToHashSet();
        var toAddS = newStudios.Except(oldStudios);
        var toRemoveS = oldStudios.Except(newStudios);
        
        foreach (var sid in toAddS)
            mov.MovieStudios.Add(new MovieStudio { MovieId = mov.Id, StudioId = sid });
        mov.MovieStudios = mov.MovieStudios.Where(x => !toRemoveS.Contains(x.StudioId)).ToList();
        
        uow.Movies.Update(mov);
        await uow.SaveChangesAsync(ct);
        
        audit = new SystemDto.AuditLog
        {
            Id = Guid.NewGuid().ToString(),
            CorrelationId = (string?)HttpContext?.Request.Headers["X-Correlation-Id"] ?? HttpContext?.TraceIdentifier,
            UserId = userId,
            Username = email,
            Action = "update",
            ResourceType = "Movie",
            ResourceId = id.ToString(),
            Details = "UpdateMovie",
            ClientIp = ClientIp,
            UserAgent = UserAgent,
            Route = HttpContext?.Request.Path.ToString(),
            HttpMethod = HttpContext?.Request.Method,
            StatusCode = StatusCodes.Status200OK,
        };

        _logger.Information("Movie audit {@Audit}", audit);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        if(await uow.Movies.FindAsync(id) is not { } mov) 
            throw new NotFoundException("movie", id);

        mov.IsDeleted = true;
    }
    
    static double Jaccard(IEnumerable<int> a, IEnumerable<int> b)
    {
        var setA = a as ISet<int> ?? new HashSet<int>(a);
        var setB = b as ISet<int> ?? new HashSet<int>(b);
        if (setA.Count == 0 && setB.Count == 0) return 0;
        int inter = setA.Intersect(setB).Count();
        int union = setA.Union(setB).Count();
        return union == 0 ? 0 : (double)inter / union; // 0..1
    }

    static double YearSimilarity(int y1, int y2)
    {
        var gap = Math.Abs(y1 - y2);
        return Math.Max(0, 1 - gap / 10.0); // cách 10 năm → 0
    }
    
    static string SimilarKey(int movieId, DateTime updatedAt, int topK, bool includeVip)
        => $"similar:movie:{movieId}:{updatedAt.Ticks}:{topK}:{includeVip}";
}