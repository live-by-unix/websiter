using Websiter.Security;
using Xunit;

namespace Websiter.Security.Tests;

public class RateLimiterTests
{
    [Fact]
    public void AllowsUpToLimit_ThenBlocks()
    {
        var limiter = new RateLimiter(new RateLimitOptions { Enabled = true, RequestsPerWindow = 3, WindowSeconds = 60 });
        Assert.True(limiter.TryAcquire("ip1"));
        Assert.True(limiter.TryAcquire("ip1"));
        Assert.True(limiter.TryAcquire("ip1"));
        Assert.False(limiter.TryAcquire("ip1"));
        // Different key unaffected.
        Assert.True(limiter.TryAcquire("ip2"));
    }

    [Fact]
    public void Disabled_AlwaysAllows()
    {
        var limiter = new RateLimiter(new RateLimitOptions { Enabled = false, RequestsPerWindow = 1 });
        Assert.True(limiter.TryAcquire("a"));
        Assert.True(limiter.TryAcquire("a"));
    }

    [Fact]
    public void RetryAfter_IsPositive()
    {
        var limiter = new RateLimiter(new RateLimitOptions { Enabled = true, RequestsPerWindow = 1, WindowSeconds = 60 });
        limiter.TryAcquire("k");
        limiter.TryAcquire("k"); // now limited
        Assert.True(limiter.RetryAfterSeconds("k") > 0);
    }
}

public class ValidationTests
{
    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("not-an-email", false)]
    [InlineData("", false)]
    public void Email(string value, bool expected) => Assert.Equal(expected, Validation.IsEmail(value));

    [Theory]
    [InlineData("my-slug_123", true)]
    [InlineData("bad slug!", false)]
    [InlineData("", false)]
    public void Slug(string value, bool expected) => Assert.Equal(expected, Validation.IsSlug(value));

    [Fact]
    public void StrongPassword_RequiresLengthAndClasses()
    {
        Assert.False(Validation.IsStrongPassword("short1!"));
        Assert.False(Validation.IsStrongPassword("alllowercasepassword"));
        Assert.True(Validation.IsStrongPassword("Correct-Horse-Battery"));
    }

    [Theory]
    [InlineData("192.168.1.1", true)]
    [InlineData("::1", true)]
    [InlineData("not-an-ip", false)]
    public void IpAddress(string value, bool expected) => Assert.Equal(expected, Validation.IsIpAddress(value));

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://localhost:8080", true)]
    [InlineData("ftp://x", false)]
    [InlineData("notaurl", false)]
    public void HttpUrl(string value, bool expected) => Assert.Equal(expected, Validation.IsHttpUrl(value));
}

public class SecretsTests
{
    [Fact]
    public void EnvVar_Resolves()
    {
        Environment.SetEnvironmentVariable("WEBSITER_SECRET_TESTKEY", "s3cret");
        try
        {
            Assert.True(Secrets.TryGet("TestKey", out var value));
            Assert.Equal("s3cret", value);
            Assert.Equal("s3cret", Secrets.Get("TestKey"));
        }
        finally { Environment.SetEnvironmentVariable("WEBSITER_SECRET_TESTKEY", null); }
    }

    [Fact]
    public void MissingSecret_Throws()
    {
        Assert.False(Secrets.TryGet("DefinitelyMissingSecret12345", out _));
        Assert.Throws<InvalidOperationException>(() => Secrets.Get("DefinitelyMissingSecret12345"));
    }

    [Fact]
    public void ResolveReference_HandlesPrefixes()
    {
        Environment.SetEnvironmentVariable("WEBSITER_SECRET_REFTEST", "resolved!");
        try
        {
            Assert.Equal("resolved!", Secrets.ResolveReference("secretref:RefTest"));
            Assert.Equal("plain", Secrets.ResolveReference("plain"));
            Assert.Equal("fallback", Secrets.ResolveReference(null, "fallback"));
        }
        finally { Environment.SetEnvironmentVariable("WEBSITER_SECRET_REFTEST", null); }
    }
}
