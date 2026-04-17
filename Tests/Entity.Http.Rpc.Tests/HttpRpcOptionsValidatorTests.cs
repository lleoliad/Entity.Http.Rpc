using Entities.Http.Rpc;
using Xunit;

namespace Entity.Http.Rpc.Tests;

public sealed class HttpRpcOptionsValidatorTests
{
    [Fact]
    public void Validate_Should_Pass_For_Minimal_Default_Options()
    {
        var options = new HttpRpcOptions();

        var errors = HttpRpcOptionsValidator.Validate(options);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_Should_Fail_When_Auth_Enabled_Without_Key()
    {
        var options = new HttpRpcOptions
        {
            Cors =
            {
                Enabled = true,
                AllowedOrigins = ["https://api.example.com"]
            },
            Auth =
            {
                Enabled = true,
                Issuer = "issuer",
                Audience = "audience"
            }
        };

        var errors = HttpRpcOptionsValidator.Validate(options);

        Assert.Contains(errors, message => message.Contains("Auth.SigningKey"));
    }

    [Fact]
    public void Validate_Should_Fail_When_Cors_Enabled_Without_Origins()
    {
        var options = new HttpRpcOptions
        {
            Cors =
            {
                Enabled = true
            }
        };

        var errors = HttpRpcOptionsValidator.Validate(options);

        Assert.Contains(errors, message => message.Contains("Cors.AllowedOrigins"));
    }

    [Fact]
    public void Validate_Should_Fail_When_Proto_Enabled_Without_Header_Name()
    {
        var options = new HttpRpcOptions
        {
            Proto =
            {
                Enabled = true,
                SessionHeaderName = ""
            }
        };

        var errors = HttpRpcOptionsValidator.Validate(options);

        Assert.Contains(errors, message => message.Contains("Proto.SessionHeaderName"));
    }

    [Fact]
    public void Validate_Should_Fail_When_Proto_Timeouts_Are_Not_Positive()
    {
        var options = new HttpRpcOptions
        {
            Proto =
            {
                Enabled = true,
                SessionIdleTimeoutSeconds = 0,
                SessionCleanupIntervalSeconds = -1
            }
        };

        var errors = HttpRpcOptionsValidator.Validate(options);

        Assert.Contains(errors, message => message.Contains("Proto.SessionIdleTimeoutSeconds"));
        Assert.Contains(errors, message => message.Contains("Proto.SessionCleanupIntervalSeconds"));
    }
}
