using System.Security.Claims;
using Esi.RAG.Application;
using Esi.RAG.Domain;
using Microsoft.AspNetCore.Http;

namespace Esi.RAG.Api;

public sealed class AuthenticatedTenantContextAccessor(IHttpContextAccessor httpContextAccessor) : ITenantContextAccessor
{
    public TenantContext GetRequiredContext()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var tenantId = principal?.FindFirst("tenant_id")?.Value ?? principal?.FindFirst("tid")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId) || principal?.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException("An authenticated tenant context is required.");
        }

        return TenantContext.Create(
            tenantId,
            principal.FindAll("role").Select(claim => claim.Value),
            principal.FindAll("permission").Select(claim => claim.Value));
    }
}
