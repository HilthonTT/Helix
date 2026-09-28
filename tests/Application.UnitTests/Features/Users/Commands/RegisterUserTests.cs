using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Features.Users.Commands;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Users.Commands;

public sealed class RegisterUserTests
{
    private static readonly RegisterUser.Request Request = new(
        "Username",
        "Password",
        "Password");

    private const string RecoveryKey = "ABCDE-FGHJK-MNPQR-STVWX-YZ012";

    private readonly RegisterUser _registerUser;

    private readonly IUserRepository _userRepositoryMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly IPasswordHasher _passwordHasherMock;
    private readonly IRecoveryKeyGenerator _recoveryKeyGeneratorMock;
    private readonly ILoggedInUser _loggedInUserMock;

    public RegisterUserTests()
    {
        _userRepositoryMock = Substitute.For<IUserRepository>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _passwordHasherMock = Substitute.For<IPasswordHasher>();
        _recoveryKeyGeneratorMock = Substitute.For<IRecoveryKeyGenerator>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();

        _recoveryKeyGeneratorMock.Generate().Returns(RecoveryKey);
        _passwordHasherMock.Hash(RecoveryKey).Returns("RecoveryKeyHash");

        _registerUser = new(_userRepositoryMock, _unitOfWorkMock, _passwordHasherMock, _recoveryKeyGeneratorMock, _loggedInUserMock);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenUsernameIsNotUnique()
    {
        _userRepositoryMock.IsUsernameUniqueAsync(Arg.Is<string>(e => e == Request.Username))
            .Returns(false);

        Result<RegisterUser.Response> result = await _registerUser.Handle(Request);

        result.Error.Should().Be(AuthenticationErrors.UsernameNotUnique);
    }

    [Fact]
    public async Task Handle_Should_ReturnSuccess_WhenCreateSucceeds()
    {
        _passwordHasherMock.Hash(Arg.Is<string>(p => p == Request.Password))
            .Returns("SomeHashAbc123");

        _userRepositoryMock.IsUsernameUniqueAsync(Arg.Is<string>(e => e == Request.Username))
            .Returns(true);

        Result<RegisterUser.Response> result = await _registerUser.Handle(Request);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_CallUserRepository_WhenCreateSucceeds()
    {
        _passwordHasherMock.Hash(Arg.Is<string>(p => p == Request.Password))
            .Returns("SomeHashAbc123");

        _userRepositoryMock.IsUsernameUniqueAsync(Arg.Is<string>(e => e == Request.Username))
            .Returns(true);

         await _registerUser.Handle(Request);

        _userRepositoryMock.Received(1).Insert(Arg.Is<User>(u => u.Username == Request.Username));
    }

    [Fact]
    public async Task Handle_Should_CallUnitOfWork_WhenCreateSucceeds()
    {
        _passwordHasherMock.Hash(Arg.Is<string>(p => p == Request.Password))
            .Returns("SomeHashAbc123");

        _userRepositoryMock.IsUsernameUniqueAsync(Arg.Is<string>(e => e == Request.Username))
            .Returns(true);

        await _registerUser.Handle(Request);

        await _unitOfWorkMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_TrimTheUsername()
    {
        _userRepositoryMock.IsUsernameUniqueAsync("bob").Returns(true);
        _passwordHasherMock.Hash(Request.Password).Returns("SomeHashAbc123");

        Result<RegisterUser.Response> result = await _registerUser.Handle(Request with { Username = " bob " });

        result.IsSuccess.Should().BeTrue();
        result.Value.User.Username.Should().Be("bob");
        await _userRepositoryMock.Received(1).IsUsernameUniqueAsync("bob");
    }

    [Fact]
    public async Task Handle_Should_IssueARecoveryKey_AndStoreOnlyItsHash()
    {
        _passwordHasherMock.Hash(Request.Password).Returns("SomeHashAbc123");
        _userRepositoryMock.IsUsernameUniqueAsync(Request.Username).Returns(true);

        Result<RegisterUser.Response> result = await _registerUser.Handle(Request);

        result.Value.RecoveryKey.Should().Be(RecoveryKey);
        result.Value.User.RecoveryKeyHash.Should().Be("RecoveryKeyHash");
        result.Value.User.HasRecoveryKey.Should().BeTrue();
    }
}
