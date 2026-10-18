using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Features.Users.Commands;
using Helix.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Application.UnitTests.Features.Users.Commands;

public sealed class LoginUserTests
{
    private readonly IUserRepository _userRepositoryMock = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasherMock = Substitute.For<IPasswordHasher>();

    [Fact]
    public async Task Handle_Should_CheckAPassword_EvenForAnAccountThatDoesNotExist()
    {
        _userRepositoryMock.GetByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        LoginUser handler = Handler();

        Result<User> result = await handler.Handle(new LoginUser.Request("nobody", "Password"));

        result.Error.Should().Be(AuthenticationErrors.InvalidUsernameOrPassword);
        _passwordHasherMock.Received(1).Verify("Password", Arg.Any<string>());
    }

    private LoginUser Handler() => new(
        _userRepositoryMock,
        _passwordHasherMock,
        Substitute.For<ILoggedInUser>(),
        Substitute.For<IUnitOfWork>(),
        NullLogger<LoginUser>.Instance);
}
