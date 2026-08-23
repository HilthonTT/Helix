using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Core.Errors;
using Helix.Application.Features.Users.Commands;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Users.Commands;

public class UnlockSessionTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly UnlockSession _unlockSession;

    private readonly IUserRepository _userRepositoryMock;
    private readonly IPasswordHasher _passwordHasherMock;
    private readonly ILoggedInUser _loggedInUserMock;

    private readonly User _user;

    public UnlockSessionTests()
    {
        _userRepositoryMock = Substitute.For<IUserRepository>();
        _passwordHasherMock = Substitute.For<IPasswordHasher>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();

        _unlockSession = new(_userRepositoryMock, _passwordHasherMock, _loggedInUserMock);

        _user = User.Create("Timothy", "hashed");

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.Username.Returns("Timothy");
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _userRepositoryMock.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(_user);
    }

    [Fact]
    public async Task Handle_Should_Succeed_WhenThePasswordIsRight()
    {
        _passwordHasherMock.Verify("correct", _user.PasswordHash).Returns(true);

        Result result = await _unlockSession.Handle(new UnlockSession.Request("correct"));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenThePasswordIsWrong()
    {
        _passwordHasherMock.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        Result result = await _unlockSession.Handle(new UnlockSession.Request("guess"));

        result.Error.Should().Be(AuthenticationErrors.InvalidUsernameOrPassword);
    }

    [Fact]
    public async Task Handle_Should_LeaveTheSessionAlone_WhateverTheAnswer()
    {
        // The whole difference between locking and signing out: nothing here establishes
        // or ends a session, so the drives stay mounted and the watchdog keeps running
        // behind the lock screen.
        _passwordHasherMock.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        await _unlockSession.Handle(new UnlockSession.Request("guess"));

        _loggedInUserMock.DidNotReceive().Logout();
        _loggedInUserMock.DidNotReceive().Login(Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenNobodyIsSignedIn()
    {
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result result = await _unlockSession.Handle(new UnlockSession.Request("correct"));

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenTheAccountHasGone()
    {
        _userRepositoryMock.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((User?)null);

        Result result = await _unlockSession.Handle(new UnlockSession.Request("correct"));

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenNothingWasTyped()
    {
        Result result = await _unlockSession.Handle(new UnlockSession.Request("   "));

        result.Error.Should().Be(ValidationErrors.MissingFields);

        // Not even asked: an empty box is not a password to check.
        _passwordHasherMock.DidNotReceive().Verify(Arg.Any<string>(), Arg.Any<string>());
    }
}
