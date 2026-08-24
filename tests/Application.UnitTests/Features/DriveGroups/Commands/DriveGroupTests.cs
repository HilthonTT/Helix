using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Features.DriveGroups.Commands;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.DriveGroups.Commands;

public class DriveGroupTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

    private readonly IDriveGroupRepository _driveGroupRepositoryMock;
    private readonly IDriveRepository _driveRepositoryMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;
    private readonly IDriveMonitor _driveMonitorMock;
    private readonly IDateTimeProvider _dateTimeProviderMock;

    private readonly Drive _media;
    private readonly Drive _backups;

    public DriveGroupTests()
    {
        _driveGroupRepositoryMock = Substitute.For<IDriveGroupRepository>();
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();
        _driveMonitorMock = Substitute.For<IDriveMonitor>();
        _dateTimeProviderMock = Substitute.For<IDateTimeProvider>();

        _dateTimeProviderMock.UtcNow.Returns(Now);

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _media = Drive.Create(UserId, "Z", "192.168.0.1", "Media Vault", "Username", "Password");
        _backups = Drive.Create(UserId, "Y", "192.168.0.1", "Backups", "Username", "Password");

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([_media, _backups]);
        _driveRepositoryMock.GetAsync(UserId, Arg.Any<CancellationToken>()).Returns([_media, _backups]);

        _driveGroupRepositoryMock
            .IsNameUniqueAsync(Arg.Any<string>(), UserId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _nasConnectorMock.GetConnectedLetters().Returns([]);
        _nasConnectorMock.ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _nasConnectorMock.DisconnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    private CreateDriveGroup Create() =>
        new(_driveGroupRepositoryMock, _driveRepositoryMock, _unitOfWorkMock, _loggedInUserMock);

    private UpdateDriveGroup Update() =>
        new(_driveGroupRepositoryMock, _driveRepositoryMock, _unitOfWorkMock, _loggedInUserMock);

    private ConnectDriveGroup Connect() =>
        new(_driveGroupRepositoryMock,
            _driveRepositoryMock,
            _unitOfWorkMock,
            _loggedInUserMock,
            _nasConnectorMock,
            _driveMonitorMock,
            _dateTimeProviderMock);

    private DriveGroup GivenGroup(params Guid[] driveIds)
    {
        DriveGroup group = DriveGroup.Create(UserId, "Office", driveIds);

        _driveGroupRepositoryMock.GetByIdAsync(group.Id, Arg.Any<CancellationToken>()).Returns(group);
        _driveGroupRepositoryMock.GetByIdAsNoTrackingAsync(group.Id, Arg.Any<CancellationToken>()).Returns(group);

        return group;
    }

    [Fact]
    public async Task Create_Should_KeepOnlyTheDrivesTheUserOwns()
    {
        // The ids come from a list the presentation layer built. A group holding someone
        // else's drive is a group that silently does nothing when connected.
        Guid someoneElses = Guid.NewGuid();

        DriveGroup? inserted = null;
        _driveGroupRepositoryMock.When(r => r.Insert(Arg.Any<DriveGroup>()))
            .Do(call => inserted = call.Arg<DriveGroup>());

        Result<DriveGroup> result = await Create().Handle(
            new CreateDriveGroup.Request("Office", [_media.Id, someoneElses]));

        result.IsSuccess.Should().BeTrue();
        inserted!.DriveIds.Should().Equal(_media.Id);
    }

    [Fact]
    public async Task Create_Should_Refuse_WhenNoneOfTheDrivesExist()
    {
        Result<DriveGroup> result = await Create().Handle(
            new CreateDriveGroup.Request("Office", [Guid.NewGuid()]));

        result.Error.Should().Be(DriveGroupErrors.NoDrivesSelected);
    }

    [Fact]
    public async Task Create_Should_Refuse_ASecondGroupOfTheSameName()
    {
        _driveGroupRepositoryMock
            .IsNameUniqueAsync("Office", UserId, null, Arg.Any<CancellationToken>())
            .Returns(false);

        Result<DriveGroup> result = await Create().Handle(
            new CreateDriveGroup.Request("Office", [_media.Id]));

        result.Error.Should().Be(DriveGroupErrors.NameNotUnique("Office"));
    }

    [Fact]
    public async Task Create_Should_TrimTheName()
    {
        DriveGroup? inserted = null;
        _driveGroupRepositoryMock.When(r => r.Insert(Arg.Any<DriveGroup>()))
            .Do(call => inserted = call.Arg<DriveGroup>());

        await Create().Handle(new CreateDriveGroup.Request("  Office  ", [_media.Id]));

        inserted!.Name.Should().Be("Office");
    }

    [Fact]
    public async Task Update_Should_LetAGroupKeepItsOwnName()
    {
        // Excluded from the uniqueness check, or changing only the membership of a group
        // would be rejected for colliding with the name it already has.
        DriveGroup group = GivenGroup(_media.Id);

        await Update().Handle(new UpdateDriveGroup.Request(group.Id, "Office", [_media.Id, _backups.Id]));

        await _driveGroupRepositoryMock.Received(1)
            .IsNameUniqueAsync("Office", UserId, group.Id, Arg.Any<CancellationToken>());

        group.DriveIds.Should().Equal(_media.Id, _backups.Id);
    }

    [Fact]
    public async Task Update_Should_Refuse_AGroupOfAnotherUser()
    {
        DriveGroup group = DriveGroup.Create(Guid.NewGuid(), "Theirs", [_media.Id]);

        _driveGroupRepositoryMock.GetByIdAsync(group.Id, Arg.Any<CancellationToken>()).Returns(group);

        Result<DriveGroup> result = await Update().Handle(
            new UpdateDriveGroup.Request(group.Id, "Mine", [_media.Id]));

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }

    [Fact]
    public async Task Connect_Should_MountOnlyTheDrivesThatAreDown()
    {
        DriveGroup group = GivenGroup(_media.Id, _backups.Id);

        _nasConnectorMock.GetConnectedLetters().Returns(["Z"]);

        Result result = await Connect().Handle(new ConnectDriveGroup.Request(group.Id));

        result.IsSuccess.Should().BeTrue();

        await _nasConnectorMock.Received(1).ConnectAsync(_backups, Arg.Any<CancellationToken>());
        await _nasConnectorMock.DidNotReceive().ConnectAsync(_media, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Connect_Should_StampTheDrivesThatCameUp()
    {
        DriveGroup group = GivenGroup(_media.Id);

        await Connect().Handle(new ConnectDriveGroup.Request(group.Id));

        _media.LastConnectedOnUtc.Should().Be(Now);
        await _unitOfWorkMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Connect_Should_ReportTheDrivesThatRefused_WithoutHidingTheOnesThatWorked()
    {
        DriveGroup group = GivenGroup(_media.Id, _backups.Id);

        _nasConnectorMock.ConnectAsync(_backups, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DriveErrors.FailedToConnect("The network path was not found.")));

        Result result = await Connect().Handle(new ConnectDriveGroup.Request(group.Id));

        result.IsFailure.Should().BeTrue();
        result.Error.Description.Should().Contain("Y:").And.Contain("network path");

        // The one that worked still worked, and is stamped as such.
        _media.LastConnectedOnUtc.Should().Be(Now);
    }

    [Fact]
    public async Task Disconnect_Should_TakeDownOnlyTheDrivesThatAreUp()
    {
        DriveGroup group = GivenGroup(_media.Id, _backups.Id);

        _nasConnectorMock.GetConnectedLetters().Returns(["Z"]);

        Result result = await Connect().Handle(new ConnectDriveGroup.Request(group.Id, Disconnect: true));

        result.IsSuccess.Should().BeTrue();

        await _nasConnectorMock.Received(1).DisconnectAsync(_media, Arg.Any<CancellationToken>());
        await _nasConnectorMock.DidNotReceive().DisconnectAsync(_backups, Arg.Any<CancellationToken>());

        // Nothing came up, so nothing was stamped and there was nothing to save.
        await _unitOfWorkMock.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Connect_Should_DoNothing_WhenEveryDriveItNamedHasBeenDeleted()
    {
        // A group outlives the drives in it by design — it holds ids, not a foreign key —
        // so this is a real state, and it is not an error.
        DriveGroup group = GivenGroup(Guid.NewGuid());

        Result result = await Connect().Handle(new ConnectDriveGroup.Request(group.Id));

        result.IsSuccess.Should().BeTrue();
        await _nasConnectorMock.DidNotReceive().ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Connect_Should_MountInTheOrderTheGroupNames()
    {
        // Failures are reported to the user in this order, so it is the order they
        // arranged rather than whatever the database handed back.
        DriveGroup group = GivenGroup(_backups.Id, _media.Id);

        _nasConnectorMock.ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DriveErrors.FailedToConnect("down")));

        Result result = await Connect().Handle(new ConnectDriveGroup.Request(group.Id));

        result.Error.Description.IndexOf("Y:", StringComparison.Ordinal)
            .Should().BeLessThan(result.Error.Description.IndexOf("Z:", StringComparison.Ordinal));
    }
}
