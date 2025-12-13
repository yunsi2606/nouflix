using NouFlix.DTOs;
using NouFlix.Models.Entities;
using NouFlix.Persistence.Repositories.Interfaces;
using NouFlix.Services.Interface;
using NouFlix.Services.Payment;

namespace NouFlix.Services;

public class SubscriptionService(IUnitOfWork uow, PaymentGatewayFactory paymentFactory) : ISubscriptionService
{
    public async Task<IEnumerable<SubscriptionDtos.PlanDto>> GetPlans(CancellationToken ct = default)
    {
        var plans = await uow.SubscriptionPlans.ListAsync(ct: ct);
        return plans.Select(p => new SubscriptionDtos.PlanDto(
            p.Id,
            p.Name,
            p.Type,
            p.PriceMonthly,
            p.PriceYearly,
            p.Description,
            p.Features
        ));
    }

    public async Task<SubscriptionDtos.SubscriptionRes?> GetMySubscription(Guid userId, CancellationToken ct = default)
    {
        var sub = await uow.UserSubscriptions.GetActiveSubscriptionAsync(userId, ct);
        if (sub == null) return null;

        var plan = await uow.SubscriptionPlans.FindAsync(sub.PlanId);
        if (plan == null) return null;

        return new SubscriptionDtos.SubscriptionRes(
            sub.Id,
            plan.Name,
            plan.Type,
            sub.StartDate,
            sub.EndDate,
            sub.Status
        );
    }

    public async Task<SubscriptionDtos.SubscribeRes> Subscribe(Guid userId, Guid planId, string provider, string returnUrl, string cancelUrl, string durationType, CancellationToken ct = default)
    {
        var plan = await uow.SubscriptionPlans.FindAsync(planId);
        if (plan == null) throw new KeyNotFoundException("Plan not found");

        decimal amount;
        int durationDays;

        if (durationType.Equals("Yearly", StringComparison.OrdinalIgnoreCase))
        {
            amount = plan.PriceYearly;
            durationDays = 365;
        }
        else
        {
            amount = plan.PriceMonthly;
            durationDays = 30;
        }

        if (amount == 0)
        {
            var transaction = new Transaction
            {
                UserId = userId,
                PlanId = planId,
                Amount = 0,
                DurationDays = durationDays,
                Status = TransactionStatus.Completed,
                Note = "Free Plan Subscription"
            };
            await uow.Transactions.AddAsync(transaction, ct);

            var now = DateTime.UtcNow;
            var sub = new UserSubscription
            {
                UserId = userId,
                PlanId = planId,
                StartDate = now,
                EndDate = now.AddDays(durationDays),
                Status = SubscriptionStatus.Active
            };
            await uow.UserSubscriptions.AddAsync(sub, ct);
            await uow.SaveChangesAsync(ct);

            return new SubscriptionDtos.SubscribeRes(null, true);
        }

        // Create Pending Transaction
        var transactionPending = new Transaction
        {
            UserId = userId,
            PlanId = planId,
            Amount = amount,
            DurationDays = durationDays,
            Status = TransactionStatus.Pending,
            Note = $"Initiated via {provider} ({durationType})"
        };
        await uow.Transactions.AddAsync(transactionPending, ct);
        await uow.SaveChangesAsync(ct); // Save to get Id

        // Create Payment Session
        var gateway = paymentFactory.Create(provider);
        // Append transactionId to returnUrl so we can identify it on callback
        var callbackUrl = $"{returnUrl}?transactionId={transactionPending.Id}&provider={provider}";
        var sessionUrl = await gateway.CreatePaymentSession(amount, "VND", $"Subscription to {plan.Name} ({durationType})", callbackUrl, cancelUrl, ct);

        return new SubscriptionDtos.SubscribeRes(sessionUrl, false);
    }

    public async Task<SubscriptionDtos.SubscriptionRes> ActivateSubscription(Guid transactionId, string sessionId, CancellationToken ct = default)
    {
        var transaction = await uow.Transactions.FindAsync(transactionId);
        if (transaction == null) throw new KeyNotFoundException("Transaction not found");
        
        if (transaction.Status == TransactionStatus.Completed)
            throw new InvalidOperationException("Transaction already completed");

        // Determine provider from Note or pass it in. 
        // We appended provider to callbackUrl, so Controller should pass it.
        // But for now, let's parse it from Note or assume we pass it.
        // Let's assume we pass provider or parse it. 
        // Actually, I'll parse it from Note for simplicity or just try both?
        // Better: Pass provider to ActivateSubscription.
        // But wait, I can't change the signature easily without updating Controller first.
        // Let's assume the Controller extracts provider from query params and passes it.
        // I'll update the signature of ActivateSubscription to take provider.
        
        // Wait, I need to know which gateway to use to Verify.
        // I will parse provider from Transaction.Note "Initiated via {provider}"
        var noteParts = transaction.Note?.Split(" via ");
        var provider = noteParts?.Length > 1 ? noteParts[1] : "stripe"; // Default fallback

        var gateway = paymentFactory.Create(provider);
        var isValid = await gateway.VerifyPayment(sessionId, ct);

        if (!isValid)
        {
            transaction.Status = TransactionStatus.Failed;
            await uow.SaveChangesAsync(ct);
            throw new Exception("Payment verification failed");
        }

        transaction.Status = TransactionStatus.Completed;
        transaction.Note += $" [Paid: {sessionId}]";
        
        // Create Subscription
        var plan = await uow.SubscriptionPlans.FindAsync(transaction.PlanId);
        if (plan == null) throw new KeyNotFoundException("Plan not found"); // Should not happen

        var now = DateTime.UtcNow;
        var sub = new UserSubscription
        {
            UserId = transaction.UserId,
            PlanId = transaction.PlanId!.Value,
            StartDate = now,
            EndDate = now.AddDays(transaction.DurationDays),
            Status = SubscriptionStatus.Active
        };
        await uow.UserSubscriptions.AddAsync(sub, ct);
        
        await uow.SaveChangesAsync(ct);

        return new SubscriptionDtos.SubscriptionRes(
            sub.Id,
            plan.Name,
            plan.Type,
            sub.StartDate,
            sub.EndDate,
            sub.Status
        );
    }

    public async Task UpdatePlan(Guid id, SubscriptionDtos.UpdatePlanDto dto, CancellationToken ct = default)
    {
        var plan = await uow.SubscriptionPlans.FindAsync(id);
        if (plan == null) throw new KeyNotFoundException("Plan not found");

        plan.Name = dto.Name;
        plan.Type = dto.Type;
        plan.PriceMonthly = dto.PriceMonthly;
        plan.PriceYearly = dto.PriceYearly;
        plan.Description = dto.Description;
        plan.Features = dto.Features;

        await uow.SaveChangesAsync(ct);
    }

    public async Task DeletePlan(Guid id, CancellationToken ct = default)
    {
        var plan = await uow.SubscriptionPlans.FindAsync(id);
        if (plan == null) throw new KeyNotFoundException("Plan not found");

        uow.SubscriptionPlans.Remove(plan);
        await uow.SaveChangesAsync(ct);
    }
}
