using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MiniBanking.IntegrationTests.Support;

namespace MiniBanking.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Registered_customer_can_log_in_and_read_their_profile()
    {
        var client = factory.CreateClient();
        var email = ApiClient.UniqueEmail();

        var register = await client.PostAsJsonAsync("/api/auth/register", new { email = email.ToUpperInvariant(), fullName = "Ayşe Yılmaz", password = ApiClient.Password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var token = await client.LoginAsync(email, ApiClient.Password);
        Assert.Equal("Bearer", token.TokenType);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var me = await (await client.GetAsync("/api/me")).ReadAsync<CustomerDto>();

        Assert.Equal(email, me.Email);
        Assert.Equal("Ayşe Yılmaz", me.FullName);
    }

    [Fact]
    public async Task Registering_the_same_email_twice_is_a_conflict()
    {
        var client = factory.CreateClient();
        var email = ApiClient.UniqueEmail();
        var body = new { email, fullName = "First", password = ApiClient.Password };

        (await client.PostAsJsonAsync("/api/auth/register", body)).EnsureSuccessStatusCode();
        var second = await client.PostAsJsonAsync("/api/auth/register", body with { });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("email_taken", await second.ProblemCodeAsync());
    }

    [Fact]
    public async Task Invalid_registration_lists_every_field_problem()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new { email = "not-an-email", fullName = "", password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"email\"", body);
        Assert.Contains("\"fullName\"", body);
        Assert.Contains("\"password\"", body);
        Assert.Equal("validation_failed", await response.ProblemCodeAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Wrong_password_and_unknown_email_get_the_same_answer(bool knownEmail)
    {
        var client = factory.CreateClient();
        var email = ApiClient.UniqueEmail();
        if (knownEmail)
        {
            (await client.PostAsJsonAsync("/api/auth/register", new { email, fullName = "Known", password = ApiClient.Password })).EnsureSuccessStatusCode();
        }

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong-password-123" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_credentials", await response.ProblemCodeAsync());
    }

    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/accounts")]
    public async Task Protected_endpoints_require_a_token(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        var client = await factory.SignedInClientAsync();
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token[..^4] + "AAAA");

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
