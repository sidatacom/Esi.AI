using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Esi.AI.Studio.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
	public DbSet<LlamaSettingsEntity> LlamaSettings => Set<LlamaSettingsEntity>();
	public DbSet<OpenVinoSettingsEntity> OpenVinoSettings => Set<OpenVinoSettingsEntity>();
	public DbSet<ModelConfigurationProfileEntity> ModelConfigurationProfiles => Set<ModelConfigurationProfileEntity>();
	public DbSet<LlamaModelEntity> LlamaModels => Set<LlamaModelEntity>();
	public DbSet<ChatConversationEntity> ChatConversations => Set<ChatConversationEntity>();
	public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		modelBuilder.Entity<ModelConfigurationProfileEntity>().ToTable("LlamaConfigurationProfiles");
	}
}
