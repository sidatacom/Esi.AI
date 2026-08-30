using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Esi.AI.Studio.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
	public DbSet<ModelSettingsEntity> ModelSettings => Set<ModelSettingsEntity>();
	public DbSet<ModelConfigurationEntity> ModelConfigurations => Set<ModelConfigurationEntity>();
	public DbSet<ModelEntity> Models => Set<ModelEntity>();
	public DbSet<ChatConversationEntity> ChatConversations => Set<ChatConversationEntity>();
	public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();
	public DbSet<ModelDownloadEntity> ModelDownloads => Set<ModelDownloadEntity>();
	public DbSet<ModelMetadataEntity> ModelMetadata => Set<ModelMetadataEntity>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		modelBuilder.Entity<ModelConfigurationEntity>().ToTable("ModelConfigurations");
		modelBuilder.Entity<ModelSettingsEntity>().HasIndex(entity => entity.Backend).IsUnique();
		modelBuilder.Entity<ModelEntity>().ToTable("Models");
		modelBuilder.Entity<ModelMetadataEntity>().HasIndex(entity => entity.ModelPath).IsUnique();
	}
}
