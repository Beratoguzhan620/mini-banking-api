using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MiniBanking.Api.Logging;
using MiniBanking.IntegrationTests.Support;
using Serilog.Core;
using Serilog.Events;

namespace MiniBanking.IntegrationTests;

[Collection(ApiCollection.Name)]
public class LoggingTests(ApiFactory factory)
{
    [Fact]
    public async Task Logs_never_contain_the_password_or_the_full_email()
    {
        var sink = new CollectingSink();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<ILogEventSink>(sink)));
        var client = host.CreateClient();
        var email = ApiClient.UniqueEmail("leak");
        const string password = "very-secret-password-99";

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var rendered = sink.Events.Select(e => e.RenderMessage() + string.Join(' ', e.Properties.Values)).ToList();
        Assert.Contains(rendered, line => line.Contains("l***@example.com", StringComparison.Ordinal));
        Assert.DoesNotContain(rendered, line => line.Contains(email, StringComparison.Ordinal));
        Assert.DoesNotContain(rendered, line => line.Contains(password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_log_line_of_a_request_carries_its_correlation_id()
    {
        var sink = new CollectingSink();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<ILogEventSink>(sink)));
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "trace-me-42");

        var response = await client.GetAsync("/health/live");

        Assert.Equal("trace-me-42", response.Headers.GetValues("X-Correlation-Id").Single());
        var requestLog = Assert.Single(sink.Events, e => e.MessageTemplate.Text.StartsWith("HTTP", StringComparison.Ordinal)
            && e.Properties.TryGetValue("RequestPath", out var path) && path.ToString().Contains("/health/live", StringComparison.Ordinal));
        Assert.Equal("\"trace-me-42\"", requestLog.Properties["CorrelationId"].ToString());
    }

    [Theory]
    [InlineData("has spaces in it")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("")]
    public async Task Unsafe_correlation_ids_are_replaced(string incoming)
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", incoming);

        var response = await client.SendAsync(request);

        var returned = response.Headers.GetValues("X-Correlation-Id").Single();
        Assert.NotEqual(incoming, returned);
        Assert.Matches("^[0-9a-f]{32}$", returned);
    }

    [Fact]
    public async Task Error_responses_point_to_the_same_id()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "problem-7");

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@example.com", password = "whatever-123" });

        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("problem-7", body.GetProperty("traceId").GetString());
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();

        public void Emit(LogEvent logEvent) => Events.Enqueue(logEvent);
    }
}

public class MaskingTests
{
    [Theory]
    [InlineData("ayse.yilmaz@example.com", "a***@example.com")]
    [InlineData("x@y.org", "x***@y.org")]
    [InlineData("not-an-email", "***")]
    [InlineData(null, "***")]
    public void Emails_keep_only_the_first_letter_and_the_domain(string? email, string expected)
    {
        Assert.Equal(expected, Masking.Email(email));
    }

    [Theory]
    [InlineData("MB1234567890123456", "MB************3456")]
    [InlineData("MB12", "***")]
    public void Account_numbers_keep_the_prefix_and_last_four_digits(string number, string expected)
    {
        Assert.Equal(expected, Masking.AccountNumber(number));
    }
}
