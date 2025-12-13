using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NouFlix.Controllers;
using NouFlix.DTOs;
using NouFlix.Models.Common;
using NouFlix.Models.Entities;
using NouFlix.Persistence.Repositories.Interfaces;
using NouFlix.Services.Interface;
using Xunit;

namespace NouFlix.Tests.Controllers;

public class SubscriptionControllerTests
{
    private readonly Mock<ISubscriptionService> _subscriptionServiceMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly SubscriptionController _controller;
    private readonly Guid _testUserId = Guid.NewGuid();

    public SubscriptionControllerTests()
    {
        _subscriptionServiceMock = new Mock<ISubscriptionService>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _controller = new SubscriptionController(_subscriptionServiceMock.Object, _unitOfWorkMock.Object);
        
        // Setup authenticated user context
        var user = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
        {
            new Claim(ClaimTypes.NameIdentifier, _testUserId.ToString()),
            new Claim(ClaimTypes.Role, "User")
        }, "TestAuthentication"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task GetPlans_ReturnsAllPlans()
    {
        // Arrange
        var expectedPlans = new List<SubscriptionDtos.PlanDto>
        {
            new(Guid.NewGuid(), "Free", PlanType.Free, 0, 0, "Free plan", new List<string> { "Limited access" }),
            new(Guid.NewGuid(), "VIP", PlanType.VIP, 9.99m, 99.99m, "Premium plan", new List<string> { "Full access", "HD quality" }),
            new(Guid.NewGuid(), "SVIP", PlanType.SVIP, 19.99m, 199.99m, "Enterprise plan", new List<string> { "Full access", "4K quality", "Priority support" })
        };

        _subscriptionServiceMock
            .Setup(s => s.GetPlans(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPlans);

        // Act
        var result = await _controller.GetPlans(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        
        var response = okResult!.Value as GlobalResponse<IEnumerable<SubscriptionDtos.PlanDto>>;
        response.Should().NotBeNull();
        response!.IsSuccess.Should().BeTrue();
        response.Data.Should().BeEquivalentTo(expectedPlans);

        _subscriptionServiceMock.Verify(s => s.GetPlans(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMySubscription_WithActiveSubscription_ReturnsSubscription()
    {
        // Arrange - Controller reads userId from JWT token via User.FindFirstValue
        var expectedSubscription = new SubscriptionDtos.SubscriptionRes(
            Guid.NewGuid(),
            "VIP",
            PlanType.VIP,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMonths(1),
            SubscriptionStatus.Active
        );

        _subscriptionServiceMock
            .Setup(s => s.GetMySubscription(_testUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedSubscription);

        // Act - No userId param, controller reads from token
        var result = await _controller.GetMySubscription(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        
        var response = okResult!.Value as GlobalResponse<SubscriptionDtos.SubscriptionRes>;
        response.Should().NotBeNull();
        response!.IsSuccess.Should().BeTrue();
        response.Data.Should().BeEquivalentTo(expectedSubscription);

        _subscriptionServiceMock.Verify(s => s.GetMySubscription(_testUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMySubscription_NoActiveSubscription_ReturnsNull()
    {
        // Arrange
        _subscriptionServiceMock
            .Setup(s => s.GetMySubscription(_testUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDtos.SubscriptionRes?)null);

        // Act
        var result = await _controller.GetMySubscription(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        
        var response = okResult!.Value as GlobalResponse<SubscriptionDtos.SubscriptionRes>;
        response.Should().NotBeNull();
        response!.IsSuccess.Should().BeTrue();
        response.Data.Should().BeNull();

        _subscriptionServiceMock.Verify(s => s.GetMySubscription(_testUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Subscribe_FreePlan_ActivatesImmediately()
    {
        // Arrange
        var planId = Guid.NewGuid();
        var subscribeReq = new SubscriptionDtos.SubscribeReq(
            planId,
            "free",
            "http://localhost/success",
            "http://localhost/cancel",
            "Monthly"
        );

        var expectedResponse = new SubscriptionDtos.SubscribeRes(null, true);

        _subscriptionServiceMock
            .Setup(s => s.Subscribe(
                _testUserId,
                planId,
                subscribeReq.PaymentProvider,
                subscribeReq.ReturnUrl,
                subscribeReq.CancelUrl,
                subscribeReq.DurationType,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act - Controller reads userId from JWT token
        var result = await _controller.Subscribe(subscribeReq, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        
        var response = okResult!.Value as GlobalResponse<SubscriptionDtos.SubscribeRes>;
        response.Should().NotBeNull();
        response!.IsSuccess.Should().BeTrue();
        response.Data!.IsActivated.Should().BeTrue();
        response.Data.PaymentUrl.Should().BeNull();

        _subscriptionServiceMock.Verify(s => s.Subscribe(
            _testUserId,
            planId,
            subscribeReq.PaymentProvider,
            subscribeReq.ReturnUrl,
            subscribeReq.CancelUrl,
            subscribeReq.DurationType,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Subscribe_PaidPlan_ReturnsPaymentUrl()
    {
        // Arrange
        var planId = Guid.NewGuid();
        var subscribeReq = new SubscriptionDtos.SubscribeReq(
            planId,
            "stripe",
            "http://localhost/success",
            "http://localhost/cancel",
            "Monthly"
        );

        var paymentUrl = "https://checkout.stripe.com/pay/cs_test_123";
        var expectedResponse = new SubscriptionDtos.SubscribeRes(paymentUrl, false);

        _subscriptionServiceMock
            .Setup(s => s.Subscribe(
                _testUserId,
                planId,
                subscribeReq.PaymentProvider,
                subscribeReq.ReturnUrl,
                subscribeReq.CancelUrl,
                subscribeReq.DurationType,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.Subscribe(subscribeReq, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        
        var response = okResult!.Value as GlobalResponse<SubscriptionDtos.SubscribeRes>;
        response.Should().NotBeNull();
        response!.IsSuccess.Should().BeTrue();
        response.Data!.IsActivated.Should().BeFalse();
        response.Data.PaymentUrl.Should().Be(paymentUrl);

        _subscriptionServiceMock.Verify(s => s.Subscribe(
            _testUserId,
            planId,
            subscribeReq.PaymentProvider,
            subscribeReq.ReturnUrl,
            subscribeReq.CancelUrl,
            subscribeReq.DurationType,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ActivateSubscription_ValidTransaction_ActivatesSubscription()
    {
        // Arrange - Controller uses ActivateSubscriptionReq body
        var transactionId = Guid.NewGuid();
        var sessionId = "cs_test_123";
        var activateReq = new SubscriptionDtos.ActivateSubscriptionReq(transactionId, sessionId);

        var expectedSubscription = new SubscriptionDtos.SubscriptionRes(
            Guid.NewGuid(),
            "VIP",
            PlanType.VIP,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMonths(1),
            SubscriptionStatus.Active
        );

        _subscriptionServiceMock
            .Setup(s => s.ActivateSubscription(transactionId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedSubscription);

        // Act - Controller expects request body
        var result = await _controller.Activate(activateReq, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        
        var response = okResult!.Value as GlobalResponse<SubscriptionDtos.SubscriptionRes>;
        response.Should().NotBeNull();
        response!.IsSuccess.Should().BeTrue();
        response.Data.Should().BeEquivalentTo(expectedSubscription);

        _subscriptionServiceMock.Verify(s => s.ActivateSubscription(transactionId, sessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdatePlan_ValidData_ReturnsOkWithMessage()
    {
        // Arrange
        var planId = Guid.NewGuid();
        var updateDto = new SubscriptionDtos.UpdatePlanDto(
            "Updated VIP",
            PlanType.VIP,
            12.99m,
            120.00m,
            "Updated premium plan",
            new List<string> { "All features", "Priority support" }
        );

        _subscriptionServiceMock
            .Setup(s => s.UpdatePlan(planId, updateDto, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.UpdatePlan(planId, updateDto, CancellationToken.None);

        // Assert - Controller returns Ok with success message, not NoContent
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as GlobalResponse<string>;
        response.Should().NotBeNull();
        response!.IsSuccess.Should().BeTrue();

        _subscriptionServiceMock.Verify(s => s.UpdatePlan(planId, updateDto, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletePlan_ValidId_ReturnsOkWithMessage()
    {
        // Arrange
        var planId = Guid.NewGuid();

        _subscriptionServiceMock
            .Setup(s => s.DeletePlan(planId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.DeletePlan(planId, CancellationToken.None);

        // Assert - Controller returns Ok with success message, not NoContent
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as GlobalResponse<string>;
        response.Should().NotBeNull();
        response!.IsSuccess.Should().BeTrue();

        _subscriptionServiceMock.Verify(s => s.DeletePlan(planId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
