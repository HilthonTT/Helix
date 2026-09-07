namespace Helix.Domain.DriveGroups;

public static class DriveGroupErrors
{
    public static Error NotFound(Guid id) => Error.NotFound(
        "DriveGroup.NotFound",
        $"The group with the specified Id = '{id}' was not found.");

    public static Error NameNotUnique(string name) => Error.Conflict(
        "DriveGroup.NameNotUnique",
        $"A group called '{name}' already exists.");

    public static readonly Error NameMissing = Error.Problem(
        "DriveGroup.NameMissing",
        "A group needs a name.");

    public static readonly Error NoDrivesSelected = Error.Problem(
        "DriveGroup.NoDrivesSelected",
        "Choose at least one drive for the group.");

    public static readonly Error NoGroupsFound = Error.NotFound(
        "DriveGroup.NoGroupsFound",
        "No groups have been created yet.");
}
