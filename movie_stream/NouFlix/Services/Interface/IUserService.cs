using System.Security.Claims;
using NouFlix.DTOs;
using NouFlix.Models.Entities;

namespace NouFlix.Services.Interface;

public interface IUserService
{
    Task<SearchRes<IEnumerable<UserDto.UserRes>>> SearchAsync(string? q, int skip, int take, CancellationToken ct = default);
    Task<IEnumerable<HistoryDto.Item>> GetHistory(Guid userId, CancellationToken ct);
    Task<User> FindOrCreateExternal(string provider, string providerKey, string? email, string? avatar, ClaimsPrincipal principal);
    Task<UserDto.UserRes> UpdateProfile(Guid userId, UpdateProfileReq req, CancellationToken ct = default);
    Task UpsertHistory(Guid userId, int movieId, int? episodeId, int positionSeconds, CancellationToken ct = default);
    Task Delete(Guid id);
}
