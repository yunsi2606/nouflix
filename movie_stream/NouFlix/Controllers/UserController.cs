using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NouFlix.DTOs;
using NouFlix.Models.Common;
using NouFlix.Services.Interface;

namespace NouFlix.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UserController(IUserService svc) : Controller
{
    [HttpGet("history")]
    [Authorize]
    public async Task<IActionResult> GetHistory(CancellationToken ct)
    {
        var me = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(me))
            return Unauthorized(GlobalResponse<string>.Error("Missing sub/NameIdentifier.", StatusCodes.Status401Unauthorized));
        
        var userId = Guid.Parse(me);
        var histories = await svc.GetHistory(userId, ct);
        
        return Ok(GlobalResponse<IEnumerable<HistoryDto.Item>>.Success(histories));
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q = "",
        [FromQuery] int skip = 1,
        [FromQuery] int take = 10,
        CancellationToken ct = default)
        => Ok(GlobalResponse<SearchRes<IEnumerable<UserDto.UserRes>>>.Success(await svc.SearchAsync(q, skip, take, ct)));

    [HttpPost("history/progress")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Authorize]
    public async Task<IActionResult> UpsertHistoryProgress([FromBody] HistoryDto.UpsertReq req, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr))
            return Unauthorized(GlobalResponse<string>.Error("Missing sub/NameIdentifier.", StatusCodes.Status401Unauthorized));

        var userId = Guid.Parse(userIdStr);
        await svc.UpsertHistory(userId, req.MovieId, req.EpisodeId, req.Position, ct);
        return NoContent();
    }
    
    [HttpPut("profile/{id:guid}")]
    [Authorize]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateProfile([FromRoute] Guid id, [FromForm] UpdateProfileReq req)
    {
        var me = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!User.IsInRole("Admin") && !string.Equals(me, id.ToString(), StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var user = await svc.UpdateProfile(id, req);
        return Ok(GlobalResponse<UserDto.UserRes>.Success(user));
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete([FromRoute] Guid id)
    {
        var me = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!User.IsInRole("Admin") && !string.Equals(me, id.ToString(), StringComparison.OrdinalIgnoreCase))
            return Forbid();
        
        await svc.Delete(id);
        return NoContent();
    }
}