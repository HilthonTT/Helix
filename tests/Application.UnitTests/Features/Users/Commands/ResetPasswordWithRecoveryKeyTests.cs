using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Core.Errors;
using Helix.Application.Features.Users.Commands;
using Helix.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Application.UnitTests.Features.Users.Commands;

public sealed class ResetPasswordWithRecoveryKeyTests
{
    private const string StoredKey = "ABCDE-FGHJK-MNPQR-STVWX-YZ012";
    private const string NewKey = "01234-56789-ABCDE-FGHJK-MNPQR";

    private static readonly ResetPasswordWithRecoveryKey.Request Request = new(
        "Timothy",
        StoredKey,
        "new-password",
        "new-password");

    private readonly ResetPasswordWithRecoveryKey _handler;

    private readonly IUserRepository _userRepositoryMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly IPasswordHasher _passwordHasherMock;
    private readonly IRecoveryKeyGenerator _recoveryKeyGeneratorMock;
    private readonly ILoggedInUser _loggedInUserMock;

    private readonly User _user;

    public ResetPasswordWithRecoveryKeyTests()
    {
        _userRepositoryMock = Substitute.For<IUserRepository>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _passwordHasherMock = Substitute.For<IPasswordHasher>();
        _recoveryKeyGeneratorMock = Substitute.For<IRecoveryKeyGenerator>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();

        _handler = new(
            _userRepositoryMock,
            _unitOfWorkMock,
            _passwordHasherMock,
            _recoveryKeyGeneratorMock,
            _loggedInUserMock,
            NullLogger<ResetPasswordWithRecoveryKey>.Instance);

        _user = User.Create("Timothy", "old-password-hash");
        _user.SetRecoveryKey("stored-key-hash");

        _userRepositoryMock.GetByUsernameAsync("Timothy", Arg.Any<CancellationToken>()).Returns(_user);
        _passwordHasherMock.Verify(StoredKey, "stored-key-hash").Returns(true);
        _passwordHasherMock.Hash("new-password").Returns("new-password-hash");
        _passwordHasherMock.Hash(NewKey).Returns("new-key-hash");
        _recoveryKeyGeneratorMock.Generate().Returns(NewKey);
    }

    [Fact]
    public async Task Handle_Should_ResetThePassword_AndRotateTheKey_WhenTheKeyIsRight()
    {
        Result<ResetPasswordWithRecoveryKey.Response> result = await _handler.Handle(Request);

        result.IsSuccess.Should().BeTrue();
        result.Value.RecoveryKey.Should().Be(NewKey);
        _user.PasswordHash.Should().Be("new-password-hash");
        _user.RecoveryKeyHash.Should().Be("new-key-hash");
        await _unitOfWorkMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _loggedInUserMock.Received(1).Login(_user.Id, _user.Username);
    }

    [Theory]
    [InlineData("abcde fghjk mnpqr stvwx yz012")]
    [InlineData("ABCDEFGHJKMNPQRSTVWXYZ012")]
    [InlineData(" abcde-fghjk-mnpqr-stvwx-yzo12 ")]
    public async Task Handle_Should_AcceptTheKey_HoweverItWasTyped(string typed)
    {
        Result<ResetPasswordWithRecoveryKey.Response> result = await _handler.Handle(Request with { RecoveryKey = typed });

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenTheKeyIsWrong()
    {
        _passwordHasherMock.Verify(Arg.Any<string>(), "stored-key-hash").Returns(false);

        Result<ResetPasswordWithRecoveryKey.Response> result = await _handler.Handle(Request);

        result.Error.Should().Be(AuthenticationErrors.InvalidUsernameOrRecoveryKey);
        _user.PasswordHash.Should().Be("old-password-hash");
        await _unitOfWorkMock.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _loggedInUserMock.DidNotReceive().Login(Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenTheKeyIsNotAKeyAtAll()
    {
        Result<ResetPasswordWithRecoveryKey.Response> result = await _handler.Handle(Request with { RecoveryKey = "hunter2" });

        result.Error.Should().Be(AuthenticationErrors.InvalidUsernameOrRecoveryKey);
        _passwordHasherMock.DidNotReceive().Verify(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenTheUserHasNoKey()
    {
        _userRepositoryMock.GetByUsernameAsync("Timothy", Arg.Any<CancellationToken>())
            .Returns(User.Create("Timothy", "old-password-hash"));

        Result<ResetPasswordWithRecoveryKey.Response> result = await _handler.Handle(Request);

        result.Error.Should().Be(AuthenticationErrors.InvalidUsernameOrRecoveryKey);
    }

    [Fact]
    public async Task Handle_Should_GiveTheSameAnswer_WhenTheUserDoesNotExist()
    {
        _userRepositoryMock.GetByUsernameAsync("Timothy", Arg.Any<CancellationToken>()).Returns((User?)null);

        Result<ResetPasswordWithRecoveryKey.Response> result = await _handler.Handle(Request);

        result.Error.Should().Be(AuthenticationErrors.InvalidUsernameOrRecoveryKey);
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenTheNewPasswordsDoNotMatch()
    {
        Result<ResetPasswordWithRecoveryKey.Response> result = await _handler.Handle(Request with { ConfirmedNewPassword = "other" });

        result.Error.Should().Be(AuthenticationErrors.NewPasswordsDoNotMatch);
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenAFieldIsMissing()
    {
        Result<ResetPasswordWithRecoveryKey.Response> result = await _handler.Handle(Request with { RecoveryKey = " " });

        result.Error.Should().Be(ValidationErrors.MissingFields);
    }
}
