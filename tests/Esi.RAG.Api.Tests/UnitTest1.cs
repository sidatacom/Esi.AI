using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Esi.RAG.Api.Tests;

public class UnitTest1 : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UnitTest1(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_Endpoint_Should_Return_Ok()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Search_Endpoint_Should_Require_Query()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/search", new { query = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ask_Endpoint_Should_Require_Query()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/ask", new { query = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
