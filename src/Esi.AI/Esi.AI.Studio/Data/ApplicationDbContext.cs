using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Esi.AI.Studio.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
	public DbSet<LlamaSettingsEntity> LlamaSettings => Set<LlamaSettingsEntity>();
	public DbSet<LlamaConfigurationProfileEntity> LlamaConfigurationProfiles => Set<LlamaConfigurationProfileEntity>();
	public DbSet<LlamaModelEntity> LlamaModels => Set<LlamaModelEntity>();
	public DbSet<ChatConversationEntity> ChatConversations => Set<ChatConversationEntity>();
	public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();
}
