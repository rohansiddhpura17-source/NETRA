using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace NETRA.Tests.Web;

/// <summary>
/// Deployment-readiness configuration for reverse proxies (Phase 15). Outside Development the
/// Identity and anti-forgery cookies are Secure, so an instance behind a TLS-terminating proxy
/// must recover the original scheme from forwarded headers — and must never trust those headers
/// from an address the operator has not named.
/// </summary>
public class ForwardedHeadersConfigurationTests
{
    private static ForwardedHeadersOptions Options(WebApplicationFactory<Program> factory)
        => factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ForwardedHeadersOptions>>().Value;

    [Fact]
    public void ForwardedHeaders_AreNotTrustedWhenNoProxyIsConfigured()
    {
        using var factory = new NetraWebFactory();

        var options = Options(factory);

        // Loopback peers are pre-seeded by the framework, but with no header selected the
        // middleware reads nothing, so no client can assert a scheme or a client IP.
        options.ForwardedHeaders.Should().Be(ForwardedHeaders.None);
    }

    [Fact]
    public void ConfiguredProxies_TurnForwardedHeaderProcessingOn()
    {
        using var baseFactory = new NetraWebFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Security:TrustedProxies:0", "127.0.0.1");
            builder.UseSetting("Security:TrustedProxies:1", "10.0.0.0/8");
        });

        var options = Options(factory);

        options.ForwardedHeaders.Should().Be(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        options.KnownProxies.Should().Contain(System.Net.IPAddress.Loopback);
        options.KnownIPNetworks.Should()
            .Contain(network => network.PrefixLength == 8
                                && network.ToString().Contains("10.0.0.0"));
    }

    [Fact]
    public void MalformedProxyEntry_FailsStartupRatherThanBeingIgnored()
    {
        // A silently dropped entry would leave the operator believing TLS termination was
        // configured while the app still refused every form page.
        using var baseFactory = new NetraWebFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.UseSetting("Security:TrustedProxies:0", "proxy.internal"));

        var failure = Record.Exception(() => factory.Services);

        failure.Should().NotBeNull();
        failure!.ToString().Should().Contain("Security:TrustedProxies");
    }
}
