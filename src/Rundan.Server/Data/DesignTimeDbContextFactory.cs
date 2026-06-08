using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Rundan.Server.Data;

/// <summary>
/// Used only by the EF Core CLI (migrations). Avoids booting the whole web app at
/// design time. The connection string here is irrelevant — migrations are generated
/// from the model, and the real path is resolved at runtime in Program.cs.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=rundan-design.db")
            .Options;
        return new AppDbContext(options);
    }
}
