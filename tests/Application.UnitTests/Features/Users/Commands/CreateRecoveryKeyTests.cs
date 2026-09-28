using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Features.Users.Commands;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Users.Commands;

public sealed class CreateRecoveryKeyTests
{
    private const string NewKey = "01234-56789-ABCDE-FGHJK-MNPQR";

    private static readonly Guid UserId = Guid.NewGuid();

    private readonly CreateRecoveryKey _handler;

    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly ILoggedInUser _loggedInUserMock;

    private readonly User _user;

    public CreateRecoveryKeyTests()
    {
        IUserRepository userRepositoryMock = Substitute.For<IUserRepository>();
        IRecoveryKeyGenerator recoveryKeyGeneratorMock = Substitute.For<IRecoveryKeyGenerator>();
        IPasswordHasher passwordHasherMock = Substitute.For<IPasswordHasher>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();

        _handler = new(userRepositoryMock, _unitOfWorkMock, passwordHasherMock, recoveryKeyGeneratorMock, _loggedInUserMock);

        _user = User.Create("Timothy", "password-hash");
        _user.SetRecoveryKey("old-key-hash");

        _loggedInUserMock.IsLoggedIn.Returns(true);
        _loggedInUserMock.UserId.Returns(UserId);
        userRepositoryMock.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(_user);
        recoveryKeyGeneratorMock.Generate().Returns(NewKey);
        passwordHasherMock.Verify("correct", "password-hash").Returns(true);
        passwordHasherMock.Hash(NewKey).Returns("new-key-hash");
    }

    [Fact]
    public async Task Handle_Should_ReplaceTheKey_WhenThePasswordIsRight()
    {
        Result<string> result = await _handler.Handle(new CreateRecoveryKey.Request("correct"));

        result.Value.Should().Be(NewKey);
        _user.RecoveryKeyHash.Should().Be("new-key-hash");
        await _unitOfWorkMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_KeepTheOldKey_WhenThePasswordIsWrong()
    {
        Result<string> result = await _handler.Handle(new CreateRecoveryKey.Request("wrong"));

        result.Error.Should().Be(AuthenticationErrors.InvalidUsernameOrPassword);
        _user.RecoveryKeyHash.Should().Be("old-key-hash");
        await _unitOfWorkMock.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Refuse_WhenNobodyIsSignedIn()
    {
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result<string> result = await _handler.Handle(new CreateRecoveryKey.Request("correct"));

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }
}
