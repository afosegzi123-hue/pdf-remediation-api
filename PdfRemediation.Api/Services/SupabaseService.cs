using Supabase;

namespace PdfRemediation.Api.Services;

public class SupabaseService
{
    private Supabase.Client? _client;
    private readonly string _url;
    private readonly string _key;

    public SupabaseService(IConfiguration config)
    {
        _url = config["SUPABASE_URL"] ?? "";
        _key = config["SUPABASE_SERVICE_KEY"] ?? "";
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(_url) || string.IsNullOrEmpty(_key))
        {
            Console.WriteLine("Supabase credentials not configured — storage disabled.");
            return;
        }

        var options = new SupabaseOptions { AutoConnectRealtime = false };
        _client = new Client(_url, _key, options);
        await _client.InitializeAsync();
    }

    public async Task<string> UploadFileAsync(string fileName, byte[] fileBytes)
    {
        if (_client == null) return "";

        try
        {
            var storage = _client.Storage.From("remediated-pdfs");
            await storage.Upload(fileBytes, fileName);
            return storage.GetPublicUrl(fileName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Supabase upload failed: {ex.Message}");
            return "";
        }
    }

    public async Task<List<object>> ListFilesAsync()
    {
        if (_client == null) throw new Exception("Supabase client is null. SUPABASE_URL or SUPABASE_SERVICE_KEY environment variables are missing or empty!");

        var files = await _client.Storage.From("remediated-pdfs").List();
        return files?.Cast<object>().ToList() ?? new List<object>();
    }

    public async Task DeleteFileAsync(string fileName)
    {
        if (_client == null) return;

        try
        {
            await _client.Storage.From("remediated-pdfs").Remove(new List<string> { fileName });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Supabase delete failed: {ex.Message}");
        }
    }
}
