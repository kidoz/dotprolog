namespace DotProlog.LanguageServer.Tests;

// A scripted client that waits for each accepted update to settle before sending its next frame.
// Separate scheduler/duplex tests exercise bursts and concurrent requests without this pacing.
internal sealed class AnalysisInputStream(IReadOnlyList<byte[]> frames, Func<Task> waitForAnalysis) : Stream
{
    private int _frame;
    private int _offset;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_offset == 0)
            await waitForAnalysis().WaitAsync(cancellationToken);
        if (_frame == frames.Count)
            return 0;
        byte[] frame = frames[_frame];
        int count = Math.Min(buffer.Length, frame.Length - _offset);
        frame.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        if (_offset == frame.Length)
        {
            _frame++;
            _offset = 0;
        }
        return count;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush() => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
