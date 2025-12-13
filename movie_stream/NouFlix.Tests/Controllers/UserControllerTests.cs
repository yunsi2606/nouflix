using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NouFlix.Controllers;
using NouFlix.DTOs;
using NouFlix.Models.Common;
using NouFlix.Services.Interface;
using Xunit;

namespace NouFlix.Tests.Controllers;

public class UserControllerTests
{
    private readonly Mock<IUserService> _userServiceMock;
    private readonly UserController _controller;
    private readonly Guid _testUserId = Guid.NewGuid();

    public UserControllerTests()
    {
        _userServiceMock = new Mock<IUserService>();
        _controller = new UserController(_userServiceMock.Object);

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
    public async Task GetHistory_ReturnsHistoryList()
    {
        // Arrange
        var expectedHistory = new List<HistoryDto.Item>
        {
            new HistoryDto.Item(1, DateTime.Now, 100, false)
        };

        _userServiceMock.Setup(s => s.GetHistory(_testUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedHistory);

        // Act
        var result = await _controller.GetHistory(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as GlobalResponse<IEnumerable<HistoryDto.Item>>;
        response.Should().NotBeNull();
        response!.IsSuccess.Should().BeTrue();
        response.Data.Should().BeEquivalentTo(expectedHistory);
    }

    [Fact]
    public async Task UpdateProfile_ReturnsUpdatedProfile_WhenAuthorized()
    {
        // Arrange
        var req = new UpdateProfileReq("John", "Doe", null, null);
        var expectedUser = new UserDto.UserRes(
            _testUserId, 
            "john.doe@example.com", 
            "John", 
            "Doe", 
            "avatar", 
            null, 
            "User", 
            false, 
            DateTime.Now, 
            new List<HistoryDto.Item>()
        );

        _userServiceMock.Setup(s => s.UpdateProfile(_testUserId, It.IsAny<UpdateProfileReq>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedUser);

        // Act
        var result = await _controller.UpdateProfile(_testUserId, req);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as GlobalResponse<UserDto.UserRes>;
        response.Should().NotBeNull();
        response!.IsSuccess.Should().BeTrue();
        response.Data.Should().BeEquivalentTo(expectedUser);
    }

    [Fact]
    public async Task UpdateProfile_ReturnsForbid_WhenUpdatingOtherUserAndNotAdmin()
    {
        // Arrange
        var otherUserId = Guid.NewGuid();
        var req = new UpdateProfileReq { FirstName = "Jane" };

        // Act
        var result = await _controller.UpdateProfile(otherUserId, req);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }
}
