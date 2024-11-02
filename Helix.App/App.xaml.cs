using Helix.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using AppBase = Microsoft.Maui.Controls.Application;

namespace Helix.App;

public sealed partial class App : AppBase
{
    private readonly AppDbContext _appDbContext;

    public static IServiceProvider ServiceProvider { get; private set; } = default!;

    public App(IServiceProvider serviceProvider, AppDbContext appDbContext)
    {
        InitializeComponent();

        MainPage = new AppShell();

        ServiceProvider = serviceProvider;
        _appDbContext = appDbContext;

        MigrateDatabase();
    }

    private void MigrateDatabase()
    {
        Task.Run(async () =>
        {
            await _appDbContext.Database.MigrateAsync();
        });
    }
}
