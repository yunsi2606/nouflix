using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NouFlix.Controllers;
using NouFlix.DTOs;
using NouFlix.Models.Common;
using NouFlix.Models.ValueObject;
using NouFlix.Services.Interface;
using Xunit;

namespace NouFlix.Tests.Controllers;

public class MovieControllerTests
{
    private readonly Mock<IMovieService> _movieServiceMock;
    private readonly MovieController _controller;

    public MovieControllerTests()
    {
        _movieServiceMock = new Mock<IMovieService>();
        _controller = new MovieController(_movieServiceMock.Object);
    }

    [Fact]
    public async Task GetById_ReturnsMovie_WhenFound()
    {
        // Arrange
        var movieId = 1;
        var expectedMovie = new MovieDetailRes(
            movieId, 
            "test-movie", 
            "Test Movie", 
            "Test", 
            "Synopsis", 
            "poster.jpg", 
            "backdrop.jpg", 
            DateTime.Now, 
            "Director", 
            120, 
            8.5f, 
            "PG-13", 
            100, 
            50, 
            new List<GenreDto.GenreRes>(), 
            new List<StudioRes>(), 
            "US", 
            "EN", 
            PublishStatus.Published, 
            MovieType.Single, 
            QualityLevel.High, 
            false, 
            false, 
            DateTime.Now, 
            DateTime.Now
        );

        _movieServiceMock.Setup(s => s.GetById(movieId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMovie);

        // Act
        var result = await _controller.GetById(movieId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var response = okResult!.Value as GlobalResponse<MovieDetailRes>;
        response.Should().NotBeNull();
        response!.IsSuccess.Should().BeTrue();
        response.Data.Should().BeEquivalentTo(expectedMovie);
    }

    [Fact]
    public async Task Create_ReturnsCreated_WhenValid()
    {
        // Arrange
        var req = new UpsertMovieReq(
            "New Movie",
            "Alt Title",
            "new-movie",
            "Synopsis",
            "Director",
            "US",
            "EN",
            "PG",
            DateTime.Now,
            MovieType.Single,
            PublishStatus.Published,
            QualityLevel.High,
            false,
            new List<int>(),
            new List<int>()
        );
        var newId = 10;

        _movieServiceMock.Setup(s => s.CreateAsync(req, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newId);

        // Act
        var result = await _controller.Create(req, CancellationToken.None);

        // Assert
        result.Should().BeOfType<CreatedAtActionResult>();
        var createdResult = result as CreatedAtActionResult;
        createdResult!.ActionName.Should().Be(nameof(MovieController.GetById));
        var response = createdResult.Value as GlobalResponse<int>;
        response!.Data.Should().Be(newId);
    }

    [Fact]
    public async Task Update_ReturnsNoContent_WhenValid()
    {
        // Arrange
        var id = 1;
        var req = new UpsertMovieReq(
            "Updated Movie",
            "Alt Title",
            "updated-movie",
            "Synopsis",
            "Director",
            "US",
            "EN",
            "PG",
            DateTime.Now,
            MovieType.Single,
            PublishStatus.Published,
            QualityLevel.High,
            false,
            new List<int>(),
            new List<int>()
        );

        _movieServiceMock.Setup(s => s.UpdateAsync(id, req, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.Update(id, req, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_WhenValid()
    {
        // Arrange
        var id = 1;

        _movieServiceMock.Setup(s => s.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.Delete(id, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }
}
