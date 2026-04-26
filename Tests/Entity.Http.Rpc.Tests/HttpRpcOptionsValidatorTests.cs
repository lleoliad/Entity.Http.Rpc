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
    public void Validate_Should_Fail_When_MessagePack_Enabled_Without_ContentType()
    {
        var options = new HttpRpcOptions
        {
            MessagePack =
            {
                Enabled = true,
                ContentType = ""
            }
        };

        var errors = HttpRpcOptionsValidator.Validate(options);

        Assert.Contains(errors, message => message.Contains("MessagePack.ContentType"));
    }

    [Fact]
    public void Validate_Should_Fail_When_MemoryPack_Enabled_Without_ContentType()
    {
        var options = new HttpRpcOptions
        {
            MemoryPack =
            {
                Enabled = true,
                ContentType = ""
            }
        };

        var errors = HttpRpcOptionsValidator.Validate(options);

        Assert.Contains(errors, message => message.Contains("MemoryPack.ContentType"));
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

    [Fact]
    public void Validate_Should_Fail_When_Encryption_Enabled_Without_Key()
    {
        var options = new HttpRpcOptions
        {
            Encryption =
            {
                Enabled = true
            }
        };

        var errors = HttpRpcOptionsValidator.Validate(options);

        Assert.Contains(errors, message => message.Contains("Encryption.KeyBase64"));
    }

    [Fact]
    public void Validate_Should_Fail_When_Encryption_Key_Is_Not_Thirty_Two_Bytes()
    {
        var options = new HttpRpcOptions
        {
            Encryption =
            {
                Enabled = true,
                KeyBase64 = Convert.ToBase64String(new byte[16])
            }
        };

        var errors = HttpRpcOptionsValidator.Validate(options);

        Assert.Contains(errors, message => message.Contains("Encryption.KeyBase64"));
    }

    [Fact]
    public void Validate_Should_Fail_When_Encryption_Algorithm_Is_Unsupported()
    {
        var options = new HttpRpcOptions
        {
            Encryption =
            {
                Enabled = true,
                Algorithm = "AesCbc",
                KeyBase64 = Convert.ToBase64String(new byte[32])
            }
        };

        var errors = HttpRpcOptionsValidator.Validate(options);

        Assert.Contains(errors, message => message.Contains("Encryption.Algorithm"));
    }

    [Fact]
    public void Validate_Should_Fail_When_Encryption_Status_Code_Is_Invalid()
    {
        var options = new HttpRpcOptions
        {
            Encryption =
            {
                Enabled = true,
                KeyBase64 = Convert.ToBase64String(new byte[32]),
                DecryptionFailureStatusCode = 42
            }
        };

        var errors = HttpRpcOptionsValidator.Validate(options);

        Assert.Contains(errors, message => message.Contains("Encryption.DecryptionFailureStatusCode"));
    }
}
