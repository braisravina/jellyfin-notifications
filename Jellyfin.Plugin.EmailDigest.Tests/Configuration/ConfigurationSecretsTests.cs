using System;
using System.Linq;
using System.Text.Json;
using System.Xml.Serialization;
using System.IO;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Configuration;

/// <summary>
/// Jellyfin's generic configuration endpoint returns the configuration object verbatim, so nothing
/// secret may ever be a property of it.
/// </summary>
public sealed class ConfigurationSecretsTests
{
    [Fact]
    public void PluginConfiguration_HasNoSecretBearingProperty()
    {
        var names = typeof(PluginConfiguration).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(names, n => n.Contains("Password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Json_Serialization_ContainsNoPassword()
    {
        var json = JsonSerializer.Serialize(new PluginConfiguration { SmtpUsername = "mailer", SmtpHost = "smtp.example.com" });

        Assert.DoesNotContain("assword", json, StringComparison.Ordinal);
        Assert.Contains("SmtpUsername", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Xml_Serialization_ContainsNoPassword()
    {
        using var writer = new StringWriter();
        new XmlSerializer(typeof(PluginConfiguration)).Serialize(writer, new PluginConfiguration());

        Assert.DoesNotContain("assword", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyXmlWithAPasswordElement_IsReadWithoutErrorAndTheValueIsDropped()
    {
        const string Xml = "<?xml version=\"1.0\"?><PluginConfiguration><SmtpHost>smtp.example.com</SmtpHost><SmtpPassword>old-secret</SmtpPassword></PluginConfiguration>";

        var config = (PluginConfiguration?)new XmlSerializer(typeof(PluginConfiguration)).Deserialize(new StringReader(Xml));

        Assert.NotNull(config);
        Assert.Equal("smtp.example.com", config!.SmtpHost);
        Assert.DoesNotContain("old-secret", JsonSerializer.Serialize(config), StringComparison.Ordinal);
    }

    [Fact]
    public void JsonBodyWithAPasswordField_IsIgnoredByTheGenericUpdateEndpoint()
    {
        const string Body = "{\"SmtpHost\":\"smtp.example.com\",\"SmtpPassword\":\"injected\"}";

        var config = JsonSerializer.Deserialize<PluginConfiguration>(Body);

        Assert.NotNull(config);
        Assert.DoesNotContain("injected", JsonSerializer.Serialize(config), StringComparison.Ordinal);
    }
}
