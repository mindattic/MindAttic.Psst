namespace MindAttic.Psst.Tests.Sms;

using System.Net;
using System.Net.Http;
using System.Text;
using MindAttic.Psst.Configuration;
using MindAttic.Psst.Sms;
using Xunit;

public class TwilioSmsClientTests
{
    private static readonly TwilioSettings Settings =
        new("ACabc123", "secrettoken", "+15555550100");

    [Fact]
    public async Task SendAsync_2xxResponse_ReturnsSuccess()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"sid\":\"SM1\"}") });
        var http = new HttpClient(handler);

        var client = new TwilioSmsClient(http, Settings, "+15555550101");
        var result = await client.SendAsync("hello");

        Assert.True(result.Success);
        Assert.Equal("Twilio", result.Transport);
        Assert.Equal("queued", result.Detail);
    }

    [Fact]
    public async Task SendAsync_HitsCorrectUrl()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var http = new HttpClient(handler);

        await new TwilioSmsClient(http, Settings, "+15555550101").SendAsync("x");

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(
            "https://api.twilio.com/2010-04-01/Accounts/ACabc123/Messages.json",
            handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
    }

    [Fact]
    public async Task SendAsync_SendsBasicAuthHeader()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var http = new HttpClient(handler);

        await new TwilioSmsClient(http, Settings, "+15555550101").SendAsync("x");

        var auth = handler.LastRequest!.Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Basic", auth!.Scheme);
        var expected = Convert.ToBase64String(Encoding.ASCII.GetBytes("ACabc123:secrettoken"));
        Assert.Equal(expected, auth.Parameter);
    }

    [Fact]
    public async Task SendAsync_PostsFromToAndBodyFormFields()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var http = new HttpClient(handler);

        await new TwilioSmsClient(http, Settings, "+15555550101")
            .SendAsync("psst: done in 3.2s");

        var pairs = handler.LastRequestBody!.Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));

        Assert.Equal("+15555550100", pairs["From"]);
        Assert.Equal("+15555550101", pairs["To"]);
        Assert.Equal("psst: done in 3.2s", pairs["Body"]);
    }

    [Fact]
    public async Task SendAsync_NonSuccessStatus_ReturnsFailureWithDetail()
    {
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"code\":20003,\"message\":\"Authentication Error\"}"),
            });
        var http = new HttpClient(handler);

        var result = await new TwilioSmsClient(http, Settings, "+15555550101").SendAsync("x");

        Assert.False(result.Success);
        Assert.Equal("Twilio", result.Transport);
        Assert.Contains("HTTP 401", result.Detail);
        Assert.Contains("Authentication Error", result.Detail);
    }

    [Fact]
    public async Task SendAsync_NetworkException_ReturnsFailureWithMessage()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("dns boom"));
        var http = new HttpClient(handler);

        var result = await new TwilioSmsClient(http, Settings, "+15555550101").SendAsync("x");

        Assert.False(result.Success);
        Assert.Equal("dns boom", result.Detail);
    }

    [Fact]
    public async Task SendAsync_TruncatesLongErrorBody()
    {
        var huge = new string('x', 1000);
        var handler = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(huge) });
        var http = new HttpClient(handler);

        var result = await new TwilioSmsClient(http, Settings, "+15555550101").SendAsync("x");

        Assert.False(result.Success);
        // 200-char body + "HTTP 500: " prefix + truncation ellipsis — well under the full 1000.
        Assert.True(result.Detail!.Length < 250, $"detail length was {result.Detail.Length}");
        Assert.EndsWith("…", result.Detail);
    }

    [Fact]
    public void TransportName_IsTwilio()
    {
        var client = new TwilioSmsClient(new HttpClient(), Settings, "+15555550101");
        Assert.Equal("Twilio", client.TransportName);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Snapshot the body now — the SUT disposes its HttpRequestMessage when
            // its `using` block ends, which also disposes the HttpContent.
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            LastRequest = request;
            return _responder(request);
        }
    }
}
