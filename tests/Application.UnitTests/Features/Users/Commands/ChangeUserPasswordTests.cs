using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Core.Errors;
using Helix.Application.Features.Users.Commands;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Users.Commands;

public class ChangeUserPasswordTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly ChangeUserPassword.Request ValidRequest = new("current-password", "new-password", "new-password");

    private readonly ChangeUserPassword _handler;

    private readonly IUserRepository _userRepositoryMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly IPasswordHasher _passwordHasherMock;

    private readonly User _user;

    public ChangeUserPasswordTests()
    {
        _userRepositoryMock = Substitute.For<IUserRepository>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _passwordHasherMock = Substitute.For<IPasswordHasher>();

        _handler = new(_userRepositoryMock, _unitOfWorkMock, _loggedInUserMock, _passwordHasherMock);

        _user = User.Create("Timothy", "old-hash");

        _loggedInUserMock.IsLoggedIn.Returns(true);
        _loggedInUserMock.UserId.Returns(UserId);

        _userRepositoryMock.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(_user);

        _passwordHasherMock.Verify("current-password", "old-hash").Returns(true);
        _passwordHasherMock.Hash("new-password").Returns("new-hash");
    }

    [Fact]
    public async Task Handle_Should_StoreTheNewHash_AndSave()
    {
        Result result = await _handler.Handle(ValidRequest);

        result.IsSuccess.Should().BeTrue();
        _user.PasswordHash.Should().Be("new-hash");
        await _unitOfWorkMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "new-password", "new-password")]
    [InlineData("current-password", " ", "new-password")]
    [InlineData("current-password", "new-password", "")]
    public async Task Handle_Should_Fail_WhenAFieldIsMissing(string current, string next, string confirmed)
    {
        Result result = await _handler.Handle(new ChangeUserPassword.Request(current, next, confirmed));

        result.Error.Should().Be(ValidationErrors.MissingFields);
        await AssertNothingChanged();
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenNobodyIsSignedIn()
    {
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result result = await _handler.Handle(ValidRequest);

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
        await AssertNothingChanged();
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenTheNewPasswordsDiffer()
    {
        Result result = await _handler.Handle(new ChangeUserPassword.Request("current-password", "new-password", "new-passw0rd"));

        result.Error.Should().Be(AuthenticationErrors.NewPasswordsDoNotMatch);
        await AssertNothingChanged();
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenTheNewPasswordIsTooShort()
    {
        Result result = await _handler.Handle(new ChangeUserPassword.Request("current-password", "short", "short"));

        result.Error.Should().Be(AuthenticationErrors.PasswordTooShort);
        await AssertNothingChanged();
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenTheAccountHasGone()
    {
        _userRepositoryMock.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((User?)null);

        Result result = await _handler.Handle(ValidRequest);

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
        await AssertNothingChanged();
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenTheCurrentPasswordIsWrong()
    {
        _passwordHasherMock.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        Result result = await _handler.Handle(ValidRequest);

        result.Error.Should().Be(AuthenticationErrors.InvalidUsernameOrPassword);
        _user.PasswordHash.Should().Be("old-hash");
        await AssertNothingChanged();
    }

    private async Task AssertNothingChanged()
    {
        _passwordHasherMock.DidNotReceive().Hash(Arg.Any<string>());
        await _unitOfWorkMock.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
