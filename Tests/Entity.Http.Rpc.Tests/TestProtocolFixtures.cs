using MemoryPack;

namespace Entity.Http.Rpc.Tests;

internal static class OuterOpcode
{
    public const uint C2G_TestEmptyMessage = 134_217_729;
    public const uint C2G_TestRequest = 268_445_457;
    public const uint G2C_TestMemoryPackResponse = 419_440_402;
}

public sealed class C2G_TestRequest : IDisposable
{
    public string? Tag { get; set; }
    public int[] Data { get; set; } = [];

    public void Dispose()
    {
    }
}

[MemoryPackable]
public sealed partial class G2C_TestMemoryPackResponse : IDisposable
{
    public uint ErrorCode { get; set; }
    public TestMemoryPackInfo? Info { get; set; }

    public void Dispose()
    {
    }
}

[MemoryPackable]
public sealed partial class TestMemoryPackInfo
{
    public string? A { get; set; }
}
