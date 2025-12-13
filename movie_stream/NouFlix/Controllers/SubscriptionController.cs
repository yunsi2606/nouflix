using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NouFlix.DTOs;
using NouFlix.Models.Common;
using NouFlix.Models.Entities;
using NouFlix.Persistence.Repositories.Interfaces;
using NouFlix.Services.Interface;

namespace NouFlix.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SubscriptionController(ISubscriptionService service, IUnitOfWork uow) : ControllerBase
{
    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans(CancellationToken ct)
    {
        var plans = await service.GetPlans(ct);
        return Ok(GlobalResponse<IEnumerable<SubscriptionDtos.PlanDto>>.Success(plans));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMySubscription(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var sub = await service.GetMySubscription(userId, ct);
        return Ok(GlobalResponse<SubscriptionDtos.SubscriptionRes>.Success(sub));
    }

    [Authorize]
    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] SubscriptionDtos.SubscribeReq req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            var res = await service.Subscribe(userId, req.PlanId, req.PaymentProvider, req.ReturnUrl, req.CancelUrl, req.DurationType, ct);
            return Ok(GlobalResponse<SubscriptionDtos.SubscribeRes>.Success(res));
        }
        catch (KeyNotFoundException)
        {
            return NotFound("Plan not found");
        }
    }

    [Authorize]
    [HttpPost("activate")]
    public async Task<IActionResult> Activate([FromBody] SubscriptionDtos.ActivateSubscriptionReq req, CancellationToken ct)
    {
        try
        {
            var sub = await service.ActivateSubscription(req.TransactionId, req.SessionId, ct);
            return Ok(GlobalResponse<SubscriptionDtos.SubscriptionRes>.Success(sub));
        }
        catch (Exception ex)
        {
            return BadRequest(GlobalResponse<string>.Error(ex.Message));
        }
    }
    
    [Authorize(Roles = "Admin")]
    [HttpPost("plans")]
    public async Task<IActionResult> CreatePlan([FromBody] SubscriptionDtos.CreatePlanDto req, CancellationToken ct)
    {
        var plan = new SubscriptionPlan
        {
            Name = req.Name,
            Type = req.Type,
            PriceMonthly = req.PriceMonthly,
            PriceYearly = req.PriceYearly,
            Description = req.Description
        };
        
        await uow.SubscriptionPlans.AddAsync(plan, ct);
        await uow.SaveChangesAsync(ct);
        
        return Ok(GlobalResponse<SubscriptionPlan>.Success(plan));
    }
    
    [Authorize(Roles = "Admin")]
    [HttpPut("plans/{id}")]
    public async Task<IActionResult> UpdatePlan(Guid id, [FromBody] SubscriptionDtos.UpdatePlanDto req, CancellationToken ct)
    {
        try
        {
            await service.UpdatePlan(id, req, ct);
            return Ok(GlobalResponse<string>.Success("Plan updated successfully"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(GlobalResponse<string>.Error("Plan not found"));
        }
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("plans/{id}")]
    public async Task<IActionResult> DeletePlan(Guid id, CancellationToken ct)
    {
        try
        {
            await service.DeletePlan(id, ct);
            return Ok(GlobalResponse<string>.Success("Plan deleted successfully"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(GlobalResponse<string>.Error("Plan not found"));
        }
    }
}
