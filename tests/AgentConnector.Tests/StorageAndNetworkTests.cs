using System.Net;
using System.Text;
using AgentConnector;
using Microsoft.AspNetCore.DataProtection;

namespace AgentConnector.Tests;

public sealed class StorageAndNetworkTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid\nidentity")]
    [InlineData("invalid\0identity")]
    public void ConnectionKeysRejectInvalidIdentifiers(string? value)
    {
        Assert.Equal("invalid_input", Assert.Throws<ConnectorException>(() => new ConnectionKey(value!, "github").Validate()).Code);
        Assert.Equal("invalid_input", Assert.Throws<ConnectorException>(() => new ConnectionKey("tenant", value!).Validate()).Code);
        Assert.Equal("invalid_input", Assert.Throws<ConnectorException>(() => new ConnectionKey("tenant", "github", value!).Validate()).Code);
    }

    [Fact]
    public void ConnectionKeysEnforceIdentifierLengthLimits()
    {
        var maximumLength = new string('a', 256);
        new ConnectionKey(maximumLength, maximumLength, maximumLength).Validate();

        var tooLong = new string('a', 257);
        Assert.Throws<ConnectorException>(() => new ConnectionKey(tooLong, "github").Validate());
        Assert.Throws<ConnectorException>(() => new ConnectionKey("tenant", tooLong).Validate());
        Assert.Throws<ConnectorException>(() => new ConnectionKey("tenant", "github", tooLong).Validate());
    }

    [Fact]
    public void CredentialStorePreservesConnectionKeyValidationCompatibility()
    {
        EncryptedFileCredentialStore.ValidateKey(new("tenant", "github"));
        Assert.Equal("invalid_input", Assert.Throws<ConnectorException>(() =>
            EncryptedFileCredentialStore.ValidateKey(new("tenant", "github", ""))).Code);
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("203.0.113.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("3fff::1", false)]
    [InlineData("2002:7f00:1::", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void FiltersPrivateAndReservedAddresses(string value, bool expected) =>
        Assert.Equal(expected, NetworkGuard.IsPublicAddress(IPAddress.Parse(value)));

    [Theory]
    [InlineData("http://api.github.com/")]
    [InlineData("https://api.github.com:8443/")]
    [InlineData("https://user:password@api.github.com/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://api.github.com/#fragment")]
    public void RejectsUnsafeEndpoints(string value) =>
        Assert.Throws<ConnectorException>(() => NetworkGuard.ValidateHttpsUri(new Uri(value)));

    [Fact]
    public async Task EncryptsCredentialsAndIsolatesTenantsAndAliases()
    {
        var directory = Path.Combine(Path.GetTempPath(), "agentconnector-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new EncryptedFileCredentialStore(new() { CredentialDirectory = directory }, new EphemeralDataProtectionProvider());
            var key = new ConnectionKey("tenant-one", "github", "work");
            var credential = new ConnectorCredential(CredentialKind.Bearer, "secret-do-not-persist-plaintext");
            await store.SetAsync(key, credential);
            Assert.Equal(credential, await store.GetAsync(key));
            Assert.Null(await store.GetAsync(key with { TenantId = "tenant-two" }));
            Assert.Null(await store.GetAsync(key with { Name = "default" }));
            var bytes = await File.ReadAllBytesAsync(Directory.GetFiles(directory, "*.credential", SearchOption.AllDirectories).Single());
            Assert.DoesNotContain(credential.Secret, Encoding.UTF8.GetString(bytes));
            var connections = new List<ConnectionInfo>();
            await foreach (var connection in store.ListAsync("tenant-one")) connections.Add(connection);
            Assert.Single(connections);
            Assert.DoesNotContain(credential.Secret, connections[0].ToString());
            await store.DeleteAsync(key);
            Assert.Null(await store.GetAsync(key));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}