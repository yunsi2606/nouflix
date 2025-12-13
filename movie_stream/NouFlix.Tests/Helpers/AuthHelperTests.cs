using Xunit;
using FluentAssertions;
using NouFlix.Helpers;

namespace NouFlix.Tests.Helpers;

/// <summary>
/// Unit tests for AuthHelper utility methods.
/// Note: AuthHelper uses SHA256 hashing which is deterministic (same input = same output).
/// </summary>
public class AuthHelperTests
{
    [Fact]
    public void HashPassword_ReturnsNonEmptyHash()
    {
        // Arrange
        var password = "Password123!";

        // Act
        var hash = AuthHelper.HashPassword(password);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().NotBe(password, "hash should not equal plain password");
    }

    [Fact]
    public void HashPassword_SamePassword_ReturnsSameHash()
    {
        // Arrange - SHA256 is deterministic, same input produces same output
        var password = "Password123!";

        // Act
        var hash1 = AuthHelper.HashPassword(password);
        var hash2 = AuthHelper.HashPassword(password);

        // Assert - SHA256 doesn't use salt, so hashes should be identical
        hash1.Should().Be(hash2, "SHA256 is deterministic");
    }

    [Fact]
    public void HashPassword_DifferentPasswords_ReturnsDifferentHashes()
    {
        // Arrange
        var password1 = "Password123!";
        var password2 = "DifferentPassword!";

        // Act
        var hash1 = AuthHelper.HashPassword(password1);
        var hash2 = AuthHelper.HashPassword(password2);

        // Assert
        hash1.Should().NotBe(hash2, "different passwords should produce different hashes");
    }

    [Fact]
    public void VerifyPassword_WithCorrectPassword_ReturnsTrue()
    {
        // Arrange
        var password = "Password123!";
        var hash = AuthHelper.HashPassword(password);

        // Act
        var result = AuthHelper.VerifyPassword(password, hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WithIncorrectPassword_ReturnsFalse()
    {
        // Arrange
        var correctPassword = "Password123!";
        var incorrectPassword = "WrongPassword!";
        var hash = AuthHelper.HashPassword(correctPassword);

        // Act
        var result = AuthHelper.VerifyPassword(incorrectPassword, hash);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("Password123!")]
    [InlineData("MySecureP@ssw0rd")]
    [InlineData("TestPassword#2024")]
    public void HashAndVerify_RoundTrip_Success(string password)
    {
        // Act
        var hash = AuthHelper.HashPassword(password);
        var isValid = AuthHelper.VerifyPassword(password, hash);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void HashPassword_ReturnsBase64EncodedString()
    {
        // Arrange
        var password = "TestPassword";

        // Act
        var hash = AuthHelper.HashPassword(password);

        // Assert - Should be valid base64
        var action = () => Convert.FromBase64String(hash);
        action.Should().NotThrow<FormatException>();
    }
}
