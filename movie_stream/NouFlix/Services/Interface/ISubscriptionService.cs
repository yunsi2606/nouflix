using NouFlix.DTOs;

namespace NouFlix.Services.Interface;

public interface ISubscriptionService
{
    Task<IEnumerable<SubscriptionDtos.PlanDto>> GetPlans(CancellationToken ct = default);
    Task<SubscriptionDtos.SubscriptionRes?> GetMySubscription(Guid userId, CancellationToken ct = default);
    Task<SubscriptionDtos.SubscribeRes> Subscribe(Guid userId, Guid planId, string provider, string returnUrl, string cancelUrl, string durationType, CancellationToken ct = default);
    Task<SubscriptionDtos.SubscriptionRes> ActivateSubscription(Guid transactionId, string sessionId, CancellationToken ct = default);
    Task UpdatePlan(Guid id, SubscriptionDtos.UpdatePlanDto dto, CancellationToken ct = default);
    Task DeletePlan(Guid id, CancellationToken ct = default);
}
