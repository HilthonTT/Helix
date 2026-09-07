using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Primitives;

public abstract class Entity
{
    protected Entity(Guid id)
    {
        Id = id;
    }

    protected Entity()
    {
    }

    [Key]
    public Guid Id { get; init; }
}
