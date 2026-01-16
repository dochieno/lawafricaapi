using LawAfrica.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace LawAfrica.API.Data
{
    /// <summary>
    /// Ensures EF Core tools can create ApplicationDbContext at design-time
    /// (migrations, update-database, etc.).
    /// </summary>
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            // Load configuration from appsettings + env vars
            var basePath = Directory.GetCurrentDirectory();

            var config = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            // Prefer standard ConnectionStrings:DefaultConnection
            var conn =
                config.GetConnectionString("DefaultConnection")
                ?? config["ConnectionStrings__DefaultConnection"]
                ?? config["DATABASE_URL"];

            if (string.IsNullOrWhiteSpace(conn))
                throw new InvalidOperationException(
                    "Missing DB connection string. Set ConnectionStrings:DefaultConnection (or env ConnectionStrings__DefaultConnection / DATABASE_URL)."
                );

            // ✅ If DATABASE_URL is in postgres URI form, convert it (optional)
            // If yours is already Npgsql format, this does nothing.
            if (conn.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
                conn.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            {
                conn = ConvertPostgresUrlToNpgsql(conn);
            }

            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(conn)
                .Options;

            return new ApplicationDbContext(opts);
        }

        private static string ConvertPostgresUrlToNpgsql(string url)
        {
            // postgres://user:pass@host:port/db?sslmode=require
            var uri = new Uri(url);

            var userInfo = uri.UserInfo.Split(':', 2);
            var user = Uri.UnescapeDataString(userInfo[0]);
            var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";

            var db = uri.AbsolutePath.TrimStart('/');

            var qb = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(uri.Query))
            {
                foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split('=', 2);
                    var k = Uri.UnescapeDataString(kv[0]);
                    var v = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
                    qb[k] = v;
                }
            }

            var ssl = qb.TryGetValue("sslmode", out var sslmode) ? sslmode : "Require";

            // Trust Server Certificate=true helps some environments
            return $"Host={uri.Host};Port={uri.Port};Database={db};Username={user};Password={pass};SSL Mode={ssl};Trust Server Certificate=true";
        }
    }
}
