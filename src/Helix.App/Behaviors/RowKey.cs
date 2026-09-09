namespace Helix.App.Behaviors;

internal enum RowKey
{
    /// <summary>Enter — do the row's main thing.</summary>
    Activate,

    /// <summary>Space — tick or untick the row.</summary>
    Select,

    /// <summary>Delete.</summary>
    Delete,

    /// <summary>F2, the rename key everywhere else.</summary>
    Edit,

    /// <summary>Ctrl+D.</summary>
    Diagnose,

    /// <summary>Ctrl+O.</summary>
    Open,
}
