using Supabase;

namespace PdfRemediation.Api.Services;

public class SupabaseService
{
    private readonly Client _client;

    public SupabaseService(IConfiguration config)
    {
        var url = config["SUPABASE_URL"];
        var key = config["SUPABASE_SERVICE_KEY"];
        
        var options = new SupabaseOptions { AutoConnectRealtime = false };
        _client = new Client(url, key, options);
    }

    public async Task InitializeAsync()
    {
        await _client.InitializeAsync();
    }

    public async Task<string> UploadFileAsync(string fileName, byte[] fileBytes)
    {
        try 
        {
            await _client.Storage.From("remediated-pdfs").Upload(fileBytes, fileName);
            return _client.Storage.From("remediated-pdfs").GetPublicUrl(fileName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Supabase upload skipped/failed: {ex.Message}");
            return "";
        }
    }
}
