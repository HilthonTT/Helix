using Helix.Domain.Schedules;
using Microsoft.EntityFrameworkCore;

namespace Helix.Infrastructure.Database.Repositories;

internal sealed class ScheduleRepository(AppDbContext context) : IScheduleRepository
{
    public Task<List<Schedule>> GetAsNoTrackingAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return context
            .Schedules
            .Where(s => s.UserId == userId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public Task<List<Schedule>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return context
            .Schedules
            .Where(s => s.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public Task<Schedule?> GetByIdAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        return context
            .Schedules
            .FirstOrDefaultAsync(s => s.Id == scheduleId, cancellationToken);
    }

    public void Insert(Schedule schedule)
    {
        context.Schedules.Add(schedule);
    }

    public void Remove(Schedule schedule)
    {
        context.Schedules.Remove(schedule);
    }
}
