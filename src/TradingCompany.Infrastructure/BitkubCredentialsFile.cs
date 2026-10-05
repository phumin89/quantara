using System.Text;

namespace TradingCompany.Infrastructure;

public static class BitkubCredentialsFile
{
    private const int MaximumBytes = 64 * 1024;
    private const int MaximumValueLength = 4096;
    private const string ApiKeyName = "BITKUB_API_KEY";
    private const string ApiSecretName = "BITKUB_API_SECRET";

    public static (string ApiKey, string ApiSecret) Read(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !Path.IsPathFullyQualified(absolutePath))
            throw new ArgumentException("An absolute path is required.", nameof(absolutePath));

        var bytes = new byte[MaximumBytes + 1];
        int length;
        try
        {
            using var stream = File.OpenRead(absolutePath);
            length = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw InvalidCredentialsFile();
        }

        if (length > MaximumBytes)
            throw InvalidCredentialsFile();

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes, 0, length);
            if (text.StartsWith('\uFEFF')) text = text[1..];
        }
        catch (DecoderFallbackException)
        {
            throw InvalidCredentialsFile();
        }

        string? apiKey = null;
        string? apiSecret = null;
        foreach (var line in text.Split('\n'))
        {
            var content = line.EndsWith('\r') ? line[..^1] : line;
            var trimmed = content.TrimStart();
            if (string.IsNullOrWhiteSpace(content) || trimmed.StartsWith('#'))
                continue;

            var equals = content.IndexOf('=');
            if (equals < 0)
                continue;

            var name = content[..equals].Trim();
            if (name is not (ApiKeyName or ApiSecretName))
                continue;

            var value = ParseValue(content[(equals + 1)..]);
            if (name == ApiKeyName)
            {
                if (apiKey is not null) throw InvalidCredentialsFile();
                apiKey = value;
            }
            else
            {
                if (apiSecret is not null) throw InvalidCredentialsFile();
                apiSecret = value;
            }
        }

        if (apiKey is null || apiSecret is null)
            throw InvalidCredentialsFile();
        return (apiKey, apiSecret);
    }

    private static string ParseValue(string value)
    {
        value = value.Trim();
        if (value.Length > 0 && (value[0] == '\'' || value[0] == '"'))
        {
            var quote = value[0];
            if (value.Length < 2 || value[^1] != quote)
                throw InvalidCredentialsFile();
            value = value[1..^1];
        }
        else if (value.Length > 0 && (value[^1] == '\'' || value[^1] == '"'))
        {
            throw InvalidCredentialsFile();
        }

        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumValueLength || value.Any(character => character < 0x20 || character > 0x7E))
            throw InvalidCredentialsFile();
        return value;
    }

    private static InvalidDataException InvalidCredentialsFile() =>
        new("Invalid credentials file.");
}
