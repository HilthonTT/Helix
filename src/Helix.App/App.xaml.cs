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

        ServiceProvider = serviceProvider;
        _appDbContext = appDbContext;

        MigrateDatabase();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }

    private void MigrateDatabase()
    {
        Task.Run(async () =>
        {
            await _appDbContext.Database.MigrateAsync();
        });
    }
}
