namespace MindAttic.Psst.Sms;

using System.Net.Http.Headers;
using System.Text;
using MindAttic.Psst.Configuration;

/// <summary>
/// Direct HTTP client for Twilio's <c>/2010-04-01/Accounts/{sid}/Messages.json</c>
/// endpoint. Avoids the Twilio SDK to keep the dependency footprint small.
/// </summary>
public sealed class TwilioSmsClient : ISmsClient
{
    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly string _toNumber;

    public string TransportName => "Twilio";

    public TwilioSmsClient(HttpClient http, TwilioSettings settings, string toNumber)
    {
        _http = http;
        _settings = settings;
        _toNumber = toNumber;
    }

    public async Task<SmsResult> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.twilio.com/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("From", _settings.From),
            new KeyValuePair<string, string>("To",   _toNumber),
            new KeyValuePair<string, string>("Body", message),
        });

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
                return new SmsResult(true, TransportName, "queued");
            return new SmsResult(false, TransportName, $"HTTP {(int)response.StatusCode}: {Truncate(body, 200)}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // User-initiated cancellation is not a transport failure — let it
            // surface to the caller.
            throw;
        }
        catch (HttpRequestException ex)
        {
            return new SmsResult(false, TransportName, ex.Message);
        }
        catch (TaskCanceledException ex)
        {
            // HttpClient timeout surfaces as TaskCanceledException with an
            // un-signaled token. Treat that as a transport failure.
            return new SmsResult(false, TransportName, $"timeout: {ex.Message}");
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
