using System.Text;

namespace DotProlog.Runtime.Tests;

/// <summary>Resource-level close behavior that cannot be induced through an ordinary disk file.</summary>
public sealed class PrologStreamTests
{
    [Fact]
    public void NormalCloseRetainsTheStreamWhenFlushingFails()
    {
        PrologStream stream = StreamWith(new FailingWriter());

        Assert.Throws<IOException>(() => stream.Close(force: false));
        Assert.True(stream.IsOpen);
    }

    [Fact]
    public void ForceCloseReclaimsTheStreamWhenFlushingFails()
    {
        PrologStream stream = StreamWith(new FailingWriter());

        Assert.True(stream.Close(force: true));
        Assert.False(stream.IsOpen);
    }

    private static PrologStream StreamWith(TextWriter writer) =>
        new(3, "failing", "write", "text", null, null, writer, null, reposition: false, permanent: false);

    private sealed class FailingWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Flush() => throw new IOException("flush failed");
    }
}
