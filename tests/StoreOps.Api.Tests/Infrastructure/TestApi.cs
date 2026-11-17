using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace StoreOps.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the whole API in memory (xUnit + WebApplicationFactory, spec §3.2 Option D). Each instance has
/// its own service provider, so the in-memory repositories start fresh for every test.
/// </summary>
internal sealed class TestApi : IDisposable
{
    private readonly Factory _factory;
    private readonly List<HttpClient> _clients = [];

    public TestApi(Action<IServiceCollection>? configureServices = null, Action<Dictionary<string, string?>>? configureSettings = null)
    {
        var settings = new Dictionary<string, string?>();
        configureSettings?.Invoke(settings);
        _factory = new Factory(configureServices, settings);
        Anonymous = Track(_factory.CreateClient());
    }

    public HttpClient Anonymous { get; }

    /// <summary>A client that sends <c>Authorization: Bearer {token}</c> (see StaffSeedData for dev tokens).</summary>
    public HttpClient As(string token)
    {
        var client = Track(_factory.CreateClient());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        _factory.Dispose();
    }

    private HttpClient Track(HttpClient client)
    {
        _clients.Add(client);
        return client;
    }

    private sealed class Factory(Action<IServiceCollection>? configureServices, Dictionary<string, string?> settings)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            if (configureServices is not null)
            {
                builder.ConfigureTestServices(configureServices);
            }
        }
    }
}
