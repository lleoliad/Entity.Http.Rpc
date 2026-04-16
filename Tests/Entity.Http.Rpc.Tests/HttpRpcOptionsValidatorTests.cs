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
}
