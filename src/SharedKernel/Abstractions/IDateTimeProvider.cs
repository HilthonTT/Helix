namespace SharedKernel.Abstractions;

public interface IDateTimeProvider
{
    public DateTime UtcNow { get; }
}
