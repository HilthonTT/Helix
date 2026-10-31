using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Core.Errors;
using Helix.Application.Features.Drives.Commands;
using Helix.Domain.Drives;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

public sealed class CreateDriveProtocolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly IDriveRepository _driveRepositoryMock = Substitute.For<IDriveRepository>();
    private readonly IUnitOfWork _unitOfWorkMock = Substitute.For<IUnitOfWork>();
    private readonly ILoggedInUser _loggedInUserMock = Substitute.For<ILoggedInUser>();
    private readonly INasConnector _nasConnectorMock = Substitute.For<INasConnector>();

    private readonly CreateDrive _createDrive;

    public CreateDriveProtocolTests()
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);
        _nasConnectorMock.GetConnectedLetters().Returns([]);
        _driveRepositoryMock
            .IsLetterUniqueAsync(Arg.Any<string>(), UserId, Arg.Any<CancellationToken>())
            .Returns(true);

        _createDrive = new(_driveRepositoryMock, _unitOfWorkMock, _loggedInUserMock, _nasConnectorMock);
    }

    [Fact]
    public async Task Handle_Should_CreateAnNfsDrive_WithoutCredentials()
    {
        var request = new CreateDrive.Request("Z", "nas.local", "/volume1/media", "", "", Protocol: DriveProtocol.Nfs);

        Result<Drive> result = await _createDrive.Handle(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Protocol.Should().Be(DriveProtocol.Nfs);
        _driveRepositoryMock.Received(1).Insert(Arg.Is<Drive>(d => d.Protocol == DriveProtocol.Nfs));
    }

    [Fact]
    public async Task Handle_Should_KeepAWebDavPort()
    {
        var request = new CreateDrive.Request(
            "Z", "nas.local", "home", "user", "password", Protocol: DriveProtocol.WebDav, Port: 5006);

        Result<Drive> result = await _createDrive.Handle(request);

        result.Value.Port.Should().Be(5006);
    }

    [Fact]
    public async Task Handle_Should_StillRequireCredentials_ForWebDav()
    {
        var request = new CreateDrive.Request("Z", "nas.local", "home", "", "", Protocol: DriveProtocol.WebDav);

        Result<Drive> result = await _createDrive.Handle(request);

        result.Error.Should().Be(ValidationErrors.MissingFields);
    }

    [Fact]
    public async Task Handle_Should_RefuseAProtocolItDoesNotKnow()
    {
        var request = new CreateDrive.Request("Z", "nas.local", "share", "user", "password", Protocol: (DriveProtocol)9);

        Result<Drive> result = await _createDrive.Handle(request);

        result.Error.Should().Be(DriveErrors.UnknownProtocol);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70_000)]
    public async Task Handle_Should_RefuseAPortOutOfRange(int port)
    {
        var request = new CreateDrive.Request(
            "Z", "nas.local", "home", "user", "password", Protocol: DriveProtocol.WebDav, Port: port);

        Result<Drive> result = await _createDrive.Handle(request);

        result.Error.Should().Be(DriveErrors.InvalidPort);
    }
}
