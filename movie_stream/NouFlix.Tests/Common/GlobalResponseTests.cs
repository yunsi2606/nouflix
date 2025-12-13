using Xunit;
using FluentAssertions;
using NouFlix.Models.Common;

namespace NouFlix.Tests.Common;

/// <summary>
/// Unit tests for GlobalResponse helper class
/// </summary>
public class GlobalResponseTests
{
    [Fact]
    public void Success_WithData_ReturnsSuccessResponse()
    {
        // Arrange
        var testData = "Test Data";

        // Act
        var response = GlobalResponse<string>.Success(testData);

        // Assert
        response.Should().NotBeNull();
        response.Data.Should().Be(testData);
        response.IsSuccess.Should().BeTrue();
        response.StatusCode.Should().Be(200);
    }

    [Fact]
    public void Success_WithCustomMessage_ReturnsResponseWithMessage()
    {
        // Arrange
        var testData = 42;
        var customMessage = "Custom success message";

        // Act
        var response = GlobalResponse<int>.Success(testData, customMessage);

        // Assert
        response.Data.Should().Be(testData);
        response.Message.Should().Be(customMessage);
        response.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Error_ReturnsErrorResponse()
    {
        // Arrange
        var errorMessage = "Something went wrong";

        // Act
        var response = GlobalResponse<string>.Error(errorMessage, 400);

        // Assert
        response.Should().NotBeNull();
        response.Data.Should().BeNull();
        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        response.Message.Should().Be(errorMessage);
    }

    [Fact]
    public void IsSuccess_For2xxStatusCodes_ReturnsTrue()
    {
        // Arrange & Act
        var response200 = new GlobalResponse<string>("data", "OK", 200);
        var response201 = new GlobalResponse<string>("data", "Created", 201);
        var response299 = new GlobalResponse<string>("data", "Custom", 299);

        // Assert
        response200.IsSuccess.Should().BeTrue();
        response201.IsSuccess.Should().BeTrue();
        response299.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void IsSuccess_ForNon2xxStatusCodes_ReturnsFalse()
    {
        // Arrange & Act
        var response400 = new GlobalResponse<string>("data", "Bad Request", 400);
        var response404 = new GlobalResponse<string>("data", "Not Found", 404);
        var response500 = new GlobalResponse<string>("data", "Internal Error", 500);

        // Assert
        response400.IsSuccess.Should().BeFalse();
        response404.IsSuccess.Should().BeFalse();
        response500.IsSuccess.Should().BeFalse();
    }
}
