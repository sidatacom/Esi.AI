using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Esi.AI.Studio.Data;

/// <summary>Creates the application database context for Entity Framework design-time operations.</summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    /// <summary>Creates a context without starting the watchdog-protected Studio host.</summary>
    /// <param name="args">The command-line arguments supplied by Entity Framework.</param>
    /// <returns>An application database context configured for the Studio database.</returns>
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var databasePath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "app.db");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath};Cache=Shared")
            .Options;

        return new ApplicationDbContext(options);
    }
}