using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Core.Extensions;
using Helix.Application.Users;
using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel;

namespace Application.UnitTests.Users;

public class RegisterUserTests
{
    private static readonly RegisterUser.Request Request = new("Username", "Password", "Password");

    private readonly RegisterUser _registerUser;

    private readonly IDbContext _dbContextMock;
    private readonly IPasswordHasher _passwordHasherMock;
    private readonly ILoggedInUser _loggedInUserMock;

    public RegisterUserTests()
    {
        _dbContextMock = Substitute.For<IDbContext>();
        _passwordHasherMock = Substitute.For<IPasswordHasher>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();

        _registerUser = new(_dbContextMock, _passwordHasherMock, _loggedInUserMock);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenUsernameIsNotUnique()
    {

    }
}
