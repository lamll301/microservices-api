using IdentityService.Services;

namespace IdentityService.Tests.Unit;

// Unit test: thuần logic, không cần Docker, chạy trong vài mili-giây.
public class TokenCodecTests
{
    [Fact]
    public void Encode_produces_url_safe_text_and_roundtrips()
    {
        // Token thật của Identity chứa các ký tự + / = không an toàn khi đặt trong URL
        const string original = "CfDJ8N+abc/def==+/xyz";

        var encoded = TokenCodec.Encode(original);

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);

        Assert.True(TokenCodec.TryDecode(encoded, out var decoded));
        Assert.Equal(original, decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("***not-base64***")]
    public void TryDecode_returns_false_for_invalid_input(string? input)
    {
        Assert.False(TokenCodec.TryDecode(input, out var token));
        Assert.Equal(string.Empty, token);
    }
}
