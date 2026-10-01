using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Esi.AI.Models;

namespace Esi.AI.Studio.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
	public DbSet<ModelSettingsEntity> ModelSettings => Set<ModelSettingsEntity>();
	public DbSet<ApplicationSettingsEntity> ApplicationSettings => Set<ApplicationSettingsEntity>();
	public DbSet<BackendRuntimeSettingsEntity> BackendRuntimeSettings => Set<BackendRuntimeSettingsEntity>();
	public DbSet<ModelConfigurationEntity> ModelConfigurations => Set<ModelConfigurationEntity>();
	public DbSet<ModelEntity> Models => Set<ModelEntity>();
	public DbSet<ChatConversationEntity> ChatConversations => Set<ChatConversationEntity>();
	public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();
	public DbSet<ModelDownloadEntity> ModelDownloads => Set<ModelDownloadEntity>();
	public DbSet<ModelMetadataEntity> ModelMetadata => Set<ModelMetadataEntity>();
	public DbSet<FlowDefinitionEntity> FlowDefinitions => Set<FlowDefinitionEntity>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		modelBuilder.Entity<ModelConfigurationEntity>().ToTable("ModelConfigurations");
		modelBuilder.Entity<ModelSettingsEntity>(entity =>
		{
			entity.HasIndex(settings => new { settings.Backend, settings.BackendVariantId }).IsUnique();
			entity.Property(settings => settings.Devices)
				.HasConversion(
					devices => JsonSerializer.Serialize(devices, (JsonSerializerOptions?)null),
					json => JsonSerializer.Deserialize<List<Device>>(json, (JsonSerializerOptions?)null) ?? new List<Device>())
				.HasColumnType("TEXT");
		});
		modelBuilder.Entity<ModelEntity>().ToTable("Models");
		modelBuilder.Entity<ModelMetadataEntity>().HasIndex(entity => entity.ModelPath).IsUnique();
		modelBuilder.Entity<FlowDefinitionEntity>().HasIndex(entity => entity.Name).IsUnique();
	}
}
