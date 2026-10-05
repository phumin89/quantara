using TradingCompany.Infrastructure;
using Xunit;

namespace TradingCompany.Domain.Tests;

public sealed class BitkubCredentialsFileTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "quantara-credential-fixtures", Guid.NewGuid().ToString("N"));
    private string Write(string text)
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, ".env");
        File.WriteAllText(file, text);
        return file;
    }

    [Theory]
    [InlineData("BITKUB_API_KEY=fake-key\nBITKUB_API_SECRET=fake-secret", "fake-key", "fake-secret")]
    [InlineData("\uFEFF# comment\r\n OTHER=x\r\n BITKUB_API_KEY = \"fake-key\" \r\nBITKUB_API_SECRET='fake=secret#literal'", "fake-key", "fake=secret#literal")]
    [InlineData("BITKUB_API_KEY=${NOT_EXPANDED}\nBITKUB_API_SECRET=$(not-executed)", "${NOT_EXPANDED}", "$(not-executed)")]
    public void Reads_literal_credentials_with_standard_whitespace_and_quotes(string content, string key, string secret)
    {
        var result = BitkubCredentialsFile.Read(Write(content));
        Assert.Equal(key, result.ApiKey);
        Assert.Equal(secret, result.ApiSecret);
    }

    [Theory]
    [InlineData("BITKUB_API_KEY=\nBITKUB_API_SECRET=fake-secret")]
    [InlineData("BITKUB_API_KEY=fake-key")]
    [InlineData("BITKUB_API_KEY=fake-key\nBITKUB_API_SECRET=' '")]
    [InlineData("BITKUB_API_KEY=fake-key\n BITKUB_API_KEY =different\nBITKUB_API_SECRET=fake-secret")]
    [InlineData("BITKUB_API_KEY=fake-key\nBITKUB_API_SECRET='fake-secret\"")]
    [InlineData("BITKUB_API_KEY=fake-key\nBITKUB_API_SECRET=fake\tsecret")]
    [InlineData("BITKUB_API_KEY=fake-key\nBITKUB_API_SECRET=秘密")]
    public void Invalid_credentials_never_expose_values_in_error(string content)
    {
        var error = Assert.Throws<InvalidDataException>(() => BitkubCredentialsFile.Read(Write(content)));
        Assert.Equal("Invalid credentials file.", error.Message);
    }

    [Fact]
    public void Missing_oversized_files_and_oversized_values_fail_closed()
    {
        Assert.Throws<InvalidDataException>(() => BitkubCredentialsFile.Read(Path.Combine(root, ".env")));
        Assert.Throws<InvalidDataException>(() => BitkubCredentialsFile.Read(Write(new string('x', 65537))));
        Assert.Throws<InvalidDataException>(() => BitkubCredentialsFile.Read(Write("BITKUB_API_KEY=" + new string('x', 4097) + "\nBITKUB_API_SECRET=fake")));
        Assert.Throws<ArgumentException>(() => BitkubCredentialsFile.Read(".env"));
    }

    public void Dispose()
    {
        // This test owns a unique fixture folder, never the owner's credential file.
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
