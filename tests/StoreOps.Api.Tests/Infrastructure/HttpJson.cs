using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Shared.Http;

namespace StoreOps.Api.Tests.Infrastructure;

internal static class HttpJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static async Task<T> GetJsonAsync<T>(this HttpClient client, string url)
    {
        using var response = await client.GetAsync(Relative(url));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<T>();
    }

    public static Task<HttpResponseMessage> GetAsync(this HttpClient client, string url) => client.GetAsync(Relative(url));

    public static Task<HttpResponseMessage> DeleteAsync(this HttpClient client, string url) => client.DeleteAsync(Relative(url));

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body)
        => client.PostAsJsonAsync(Relative(url), body, Options);

    public static Task<HttpResponseMessage> PatchJsonAsync(this HttpClient client, string url, object body)
        => client.PatchAsJsonAsync(Relative(url), body, Options);

    public static async Task<T> ReadJsonAsync<T>(this HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(Options);
        Assert.NotNull(value);
        return value;
    }

    /// <summary>Asserts the response uses the StoreOps error envelope with the given status and code.</summary>
    public static async Task<ErrorBody> AssertErrorAsync(
        this HttpResponseMessage response, HttpStatusCode status, string code, string? field = null)
    {
        Assert.Equal(status, response.StatusCode);
        var error = (await response.ReadJsonAsync<ErrorResponse>()).Error;
        Assert.Equal(code, error.Code);
        Assert.Equal((int)status, error.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        if (field is not null)
        {
            Assert.Equal(field, error.Field);
        }

        return error;
    }

    private static Uri Relative(string url) => new(url, UriKind.Relative);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        StoreOpsJson.Apply(options);
        return options;
    }
}
