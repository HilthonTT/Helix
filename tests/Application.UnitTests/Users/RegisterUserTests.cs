using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Users;
using Helix.Domain.Users;
using NSubstitute;
using SharedKernel;

namespace Application.UnitTests.Users;

public sealed class RegisterUserTests
{
    private static readonly RegisterUser.Request Request = new(
        "Username",
        "Password",
        "Password");

    private readonly RegisterUser _registerUser;

    private readonly IUserRepository _userRepositoryMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly IPasswordHasher _passwordHasherMock;
    private readonly ILoggedInUser _loggedInUserMock;

    public RegisterUserTests()
    {
        _userRepositoryMock = Substitute.For<IUserRepository>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _passwordHasherMock = Substitute.For<IPasswordHasher>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();

        _registerUser = new(_userRepositoryMock, _unitOfWorkMock, _passwordHasherMock, _loggedInUserMock);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenUsernameIsNotUnique()
    {
        // Arrange
        _userRepositoryMock.IsUsernameUniqueAsync(Arg.Is<string>(e => e == Request.Username))
            .Returns(false);

        // Act
        Result<User> result = await _registerUser.Handle(Request);

        // Assert
        result.Error.Should().Be(AuthenticationErrors.UsernameNotUnique);
    }

    [Fact]
    public async Task Handle_Should_ReturnSuccess_WhenCreateSucceeds()
    {
        // Arrange
        _passwordHasherMock.Hash(Arg.Is<string>(p => p == Request.Password))
            .Returns("SomeHashAbc123");

        _userRepositoryMock.IsUsernameUniqueAsync(Arg.Is<string>(e => e == Request.Username))
            .Returns(true);

        // Act
        Result<User> result = await _registerUser.Handle(Request);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_CallUserRepository_WhenCreateSucceeds()
    {
        // Arrange
        _passwordHasherMock.Hash(Arg.Is<string>(p => p == Request.Password))
            .Returns("SomeHashAbc123");

        _userRepositoryMock.IsUsernameUniqueAsync(Arg.Is<string>(e => e == Request.Username))
            .Returns(true);

        // Act
         await _registerUser.Handle(Request);

        // Assert
        _userRepositoryMock.Received(1).Insert(Arg.Is<User>(u => u.Username == Request.Username));
    }

    [Fact]
    public async Task Handle_Should_CallUnitOfWork_WhenCreateSucceeds()
    {
        // Arrange
        _passwordHasherMock.Hash(Arg.Is<string>(p => p == Request.Password))
            .Returns("SomeHashAbc123");

        _userRepositoryMock.IsUsernameUniqueAsync(Arg.Is<string>(e => e == Request.Username))
            .Returns(true);

        // Act
        await _registerUser.Handle(Request);

        // Assert
        await _unitOfWorkMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
