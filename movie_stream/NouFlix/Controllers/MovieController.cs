using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NouFlix.DTOs;
using NouFlix.Models.Common;
using NouFlix.Services.Interface;

namespace NouFlix.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MovieController(IMovieService svc) : Controller
{
    [HttpGet("search")]
    [AllowAnonymous]
    public async Task<IActionResult> Search(
        [FromQuery] string? q = "",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 20,
        CancellationToken ct = default) 
        => Ok(GlobalResponse<SearchRes<IEnumerable<MovieRes>>>.Success(await svc.SearchAsync(q, skip, take, ct)));
    
    [HttpGet("most-viewed")]
    [AllowAnonymous]
    public async Task<IActionResult> MostViewed([FromQuery, Range(1, 100)] int take = 12, CancellationToken ct = default)
    {
        var movies = await svc.GetMostViewed(take, ct);
        return Ok(GlobalResponse<IEnumerable<MovieRes>>.Success(movies));
    }
    
    [HttpGet("trending")]
    [AllowAnonymous]
    public async Task<IActionResult> Trending([FromQuery, Range(1, 100)] int take = 12, CancellationToken ct = default)
    {
        var movies = await svc.GetTrending(take, ct);
        return Ok(GlobalResponse<IEnumerable<MovieDetailRes>>.Success(movies));
    }
    
    [HttpGet("popular")]
    [AllowAnonymous]
    public async Task<IActionResult> Popular([FromQuery, Range(1, 100)] int take = 12, CancellationToken ct = default)
    {
        var movies = await svc.GetPopular(take, ct);
        return Ok(GlobalResponse<IEnumerable<MovieRes>>.Success(movies));
    }
    
    [HttpGet("most-rating")]
    [AllowAnonymous]
    public async Task<IActionResult> MostRating([FromQuery, Range(1, 100)] int take = 12, CancellationToken ct = default)
    {
        var movies = await svc.GetMostRating(take, ct);
        return Ok(GlobalResponse<IEnumerable<MovieRes>>.Success(movies));
    }
    
    [HttpGet("new")]
    [AllowAnonymous]
    public async Task<IActionResult> New([FromQuery, Range(1, 100)] int take = 12, CancellationToken ct = default)
    {
        var movies = await svc.GetNew(take, ct);
        return Ok(GlobalResponse<IEnumerable<MovieRes>>.Success(movies));
    }

    [HttpGet("{slug}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetDetail([FromRoute] string slug, CancellationToken ct = default)
    {
        var movie = await svc.GetBySlug(slug, ct);
        
        return Ok(GlobalResponse<MovieDetailRes>.Success(movie));
    }
    
    [HttpGet("similar/{movieId:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSimilarity([FromRoute] int movieId, CancellationToken ct = default)
    {
        var movie = await svc.GetSimilar(movieId, ct: ct);
        
        return Ok(GlobalResponse<IReadOnlyList<MovieRes>>.Success(movie));
    }

    [HttpGet("{id:int}")]
    [Authorize]
    public async Task<IActionResult> GetById([FromRoute] int id, CancellationToken ct = default)
    {
        var movie = await svc.GetById(id, ct);

        return Ok(GlobalResponse<MovieDetailRes>.Success(movie));
    }
    
    [HttpGet("genre/{genreId:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetByGenre([FromRoute] int genreId, CancellationToken ct = default)
    {
        var movie = await svc.GetByGenreAsync(genreId, ct);

        return Ok(GlobalResponse<IEnumerable<MovieRes>>.Success(movie));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] UpsertMovieReq req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var id = await svc.CreateAsync(req, ct);
        return CreatedAtAction(nameof(GetById), GlobalResponse<int>.Success(id));
    }
    
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] UpsertMovieReq req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        await svc.UpdateAsync(id, req, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete([FromRoute] int id, CancellationToken ct = default)
    {
        await svc.DeleteAsync(id, ct);
        
        return NoContent();
    }
}