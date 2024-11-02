using Microsoft.EntityFrameworkCore.Storage;

namespace Helix.Infrastructure.Database;

internal sealed class CustomRelationalCommandBuilderFactory : RelationalCommandBuilderFactory
{
    public CustomRelationalCommandBuilderFactory(RelationalCommandBuilderDependencies dependencies) 
        : base(dependencies)
    {
    }

    public override IRelationalCommandBuilder Create()
    {
        return new CustomRelationalCommandBuilder(Dependencies);
    }
}
