using NouFlix.Models.Entities;
using NouFlix.Models.ValueObject;

namespace NouFlix.Tests.Helpers;

/// <summary>
/// Helper class for generating test data and mock entities
/// </summary>
public static class TestDataGenerator
{
    public static User CreateTestUser(
        Guid? id = null,
        string email = "test@example.com",
        string password = "hashedPassword")
    {
        return new User
        {
            Id = id ?? Guid.NewGuid(),
            Email = Email.Create(email),
            Password = password,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static Movie CreateTestMovie(
        int? id = null,
        string title = "Test Movie",
        string slug = "test-movie",
        int? viewCount = 0,
        double? rating = 0.0)
    {
        return new Movie
        {
            Id = id ?? 1,
            Title = title,
            Slug = slug,
            Synopsis = "Test movie description",
            ViewCount = viewCount ?? 0,
            Rating = rating ?? 0.0,
            ReleaseDate = DateTime.UtcNow,
            Runtime = TimeSpan.FromMinutes(120),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static SubscriptionPlan CreateTestPlan(
        Guid? id = null,
        string name = "Test Plan",
        PlanType type = PlanType.Free,
        decimal priceMonthly = 0,
        decimal priceYearly = 0)
    {
        return new SubscriptionPlan
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Type = type,
            PriceMonthly = priceMonthly,
            PriceYearly = priceYearly,
            Description = $"{name} description",
            Features = new List<string> { "Feature 1", "Feature 2" }
        };
    }

    public static UserSubscription CreateTestSubscription(
        Guid userId,
        Guid planId,
        SubscriptionStatus status = SubscriptionStatus.Active)
    {
        return new UserSubscription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlanId = planId,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddMonths(1),
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public static RefreshToken CreateTestRefreshToken(
        Guid userId,
        string token = "test_refresh_token",
        bool isRevoked = false,
        DateTime? expiresAt = null)
    {
        return new RefreshToken
        {
            Token = token,
            UserId = userId,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
            IsRevoked = isRevoked,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static Transaction CreateTestTransaction(
        Guid userId,
        Guid planId,
        decimal amount = 0,
        TransactionStatus status = TransactionStatus.Pending)
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlanId = planId,
            Amount = amount,
            Status = status,
            DurationDays = 30,
            Note = "Test transaction",
            CreatedAt = DateTime.UtcNow,
        };
    }

    public static Genre CreateTestGenre(
        int? id = null,
        string name = "Action",
        string slug = "action")
    {
        return new Genre
        {
            Id = id ?? 1,
            Name = name,
        };
    }

    public static Season CreateTestSeason(
        int movieId,
        int seasonNumber = 1,
        string title = "Season 1")
    {
        return new Season
        {
            Id = 1,
            MovieId = movieId,
            Number = seasonNumber,
            Title = title
        };
    }

    public static Episode CreateTestEpisode(
        int seasonId,
        int episodeNumber = 1,
        string title = "Episode 1")
    {
        return new Episode
        {
            Id = 1,
            SeasonId = seasonId,
            Number = episodeNumber,
            Title = title,
            Synopsis = $"{title} description",
            Duration = TimeSpan.FromMinutes(45)
        };
    }
}
