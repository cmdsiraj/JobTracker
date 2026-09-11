// Lets `dotnet ef migrations add` construct a context without running the
// full app (and therefore without a real %LOCALAPPDATA% store path needed).

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobTracker.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<JobTrackerDbContext>
{
    public JobTrackerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<JobTrackerDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new JobTrackerDbContext(options);
    }
}
