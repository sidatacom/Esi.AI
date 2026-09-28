using Esi.AI.Models;
using Esi.AI.Studio.Data;
using Esi.AI.Studio.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Esi.AI.Studio.Tests;

[TestClass]
public sealed class ApplicationSettingsServiceTests
{
    [TestMethod]
    public async Task UpdateAsync_EnabledBackendIds_RoundTripsThroughJson()
    {
        var (service, directory) = await CreateServiceAsync();
        try
        {
            var settings = await service.ReadAsync();
            var expectedBackends = new[] { "openvino", "llama.sycl" };

            await service.UpdateAsync(settings with { EnabledBackendIds = expectedBackends });

            var actualSettings = await service.ReadAsync();
            CollectionAssert.AreEqual(expectedBackends, actualSettings.EnabledBackendIds!.ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task UpdateAsync_EmptyEnabledBackendIdList_ThrowsArgumentException()
    {
        var (service, directory) = await CreateServiceAsync();
        try
        {
            var settings = await service.ReadAsync();

            await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                service.UpdateAsync(settings with { EnabledBackendIds = [] }));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<(ApplicationSettingsService Service, string Directory)> CreateServiceAsync()
    {
        var directory = Directory.CreateTempSubdirectory("esi-app-settings-").FullName;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={Path.Combine(directory, "settings.db")}")
            .Options;
        var factory = new TestDbContextFactory(options);
        await using (var db = await factory.CreateDbContextAsync())
            await db.Database.EnsureCreatedAsync();

        return (new ApplicationSettingsService(factory), directory);
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }
}