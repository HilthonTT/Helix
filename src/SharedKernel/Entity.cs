using System.ComponentModel.DataAnnotations;

namespace SharedKernel;

public abstract class Entity
{
    protected Entity(Guid id)
    {
        Id = id;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Entity"/> class.
    /// </summary>
    /// <remarks>
    /// Required by EF Core.
    /// </remarks>
    protected Entity()
    {
    }

    [Key]
    public Guid Id { get; init; }
}
