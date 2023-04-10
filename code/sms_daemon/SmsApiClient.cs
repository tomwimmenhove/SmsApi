using System.Text;
using System.Text.Json;

namespace sms_daemon;

public class SmsApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public SmsApiClient(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl;
    }

    public async Task<bool> PostNewMessageAsync(NewMessageDto newMessage)
    {
        var uri = new Uri(new Uri(_baseUrl), "NewMessage");
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(newMessage),
                Encoding.UTF8, "application/json")
        };

        try
        {
            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<NewMessageResponseDto>(responseContent);
            if (result == null)
            {
                Console.Error.WriteLine($"Failed to deserialize response from SMS API");
                return false;
            }

            if (response.IsSuccessStatusCode && result.Success)
            {
                return true;
            }

            Console.Error.WriteLine($"SMS API returned with " +
                $"statuscode={response.StatusCode}, " +
                $"success={result.Success}, " +
                $"message={result.Message}");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"SMS API call threw an exceeption: {e.Message}");
        }

        return false;
    }

    public async Task<SendMessageUpdateResultsDto?> GetSendMessageUpdates(long startId)
    {
        var uri = new Uri(new Uri(_baseUrl), $"GetSendMessageUpdates?start_id={startId}");
        var request = new HttpRequestMessage(HttpMethod.Get, uri);

        try
        {
            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<SendMessageUpdateResultsDto>(responseContent);
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"SMS API call threw an exceeption: {e.Message}");
        }

        return null;
    }
}
