using System.Net;
using System.Net.Http.Json;
using System.Text;
using ClashDetection.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ClashDetection.Tests;

public class ClashApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ClashApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Post_Sample1_ReturnsExpectedOverlaps()
    {
        var client = _factory.CreateClient();
        var body = File.ReadAllText(TestSupport.DataPath("input-sample1.json"));

        var response = await client.PostAsync("/api/clashes",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actual = await response.Content.ReadFromJsonAsync<FeatureCollection>(TestSupport.Json);
        TestSupport.AssertEquivalent(TestSupport.Load("output-sample1.json"), actual!);
    }

    [Fact]
    public async Task Post_InvalidInput_Returns400WithDetails()
    {
        var client = _factory.CreateClient();
        const string invalid = """{ "type": "FeatureCollection", "features": [] }""";

        var response = await client.PostAsync("/api/clashes",
            new StringContent(invalid, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains("non-empty", problem);
    }

    [Fact]
    public async Task Post_MalformedJson_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/clashes",
            new StringContent("{ not json ", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_SameInputTwice_IsIdempotent()
    {
        var client = _factory.CreateClient();
        var body = File.ReadAllText(TestSupport.DataPath("input-sample2.json"));

        async Task<FeatureCollection> PostAsync()
        {
            var r = await client.PostAsync("/api/clashes",
                new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return (await r.Content.ReadFromJsonAsync<FeatureCollection>(TestSupport.Json))!;
        }

        var first = await PostAsync();
        var second = await PostAsync(); // should hit the cache
        TestSupport.AssertEquivalent(first, second);
    }

    [Fact]
    public async Task Get_UnknownJob_Returns404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/clashes/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
        Assert.Contains("healthy", await response.Content.ReadAsStringAsync());
    }
}
