namespace IdentityService.Services;

public class InternalApiSettings
{
    private const int MinKeyLength = 32;

    public string? Key { get; init; }

    /// <summary>Endpoint nội bộ chỉ bật khi INTERNAL_API_KEY có tối thiểu 32 ký tự.</summary>
    public bool Enabled => Key is { Length: >= MinKeyLength };

    public static InternalApiSettings FromEnvironment() => new()
    {
        Key = Environment.GetEnvironmentVariable("INTERNAL_API_KEY")
    };
}
