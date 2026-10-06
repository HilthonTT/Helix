namespace Helix.Domain.Schedules;

public interface IScheduleRepository
{
    Task<List<Schedule>> GetAsNoTrackingAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<List<Schedule>> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Schedule?> GetByIdAsync(Guid scheduleId, CancellationToken cancellationToken = default);

    void Insert(Schedule schedule);

    void Remove(Schedule schedule);
}
