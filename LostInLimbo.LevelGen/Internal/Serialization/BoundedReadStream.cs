using System;
using System.IO;

namespace LostInLimbo.LevelGen.Internal.Serialization;

/// <summary>
/// Read-only view of the next <c>length</c> bytes of an inner stream. Reading past the bound
/// reports the end of stream, so a section payload can never consume bytes of the data after it.
/// Disposing the view leaves the inner stream open.
/// </summary>
internal sealed class BoundedReadStream : Stream
{
  /// <summary>Stream the bytes are read from.</summary>
  private readonly Stream _inner;

  /// <summary>Bytes still readable through this view.</summary>
  private long _remaining;

  /// <summary>
  /// Creates a view over the next <paramref name="length"/> bytes of <paramref name="inner"/>.
  /// </summary>
  /// <param name="inner">Readable stream, positioned at the first byte of the view.</param>
  /// <param name="length">Number of bytes in the view; not negative.</param>
  public BoundedReadStream(Stream inner, long length)
  {
    _inner = inner;
    _remaining = length;
  }

  /// <summary>Bytes of the view not read yet.</summary>
  public long Remaining => _remaining;

  /// <inheritdoc/>
  public override bool CanRead => true;

  /// <inheritdoc/>
  public override bool CanSeek => false;

  /// <inheritdoc/>
  public override bool CanWrite => false;

  /// <inheritdoc/>
  public override long Length => throw new NotSupportedException();

  /// <inheritdoc/>
  public override long Position
  {
    get => throw new NotSupportedException();
    set => throw new NotSupportedException();
  }

  /// <inheritdoc/>
  public override int Read(byte[] buffer, int offset, int count)
  {
    return Read(buffer.AsSpan(offset, count));
  }

  /// <inheritdoc/>
  public override int Read(Span<byte> buffer)
  {
    if (_remaining == 0) return 0;
    var allowed = (int)Math.Min(buffer.Length, _remaining);
    var read = _inner.Read(buffer.Slice(0, allowed));
    _remaining -= read;
    return read;
  }

  /// <inheritdoc/>
  public override void Flush()
  {
  }

  /// <inheritdoc/>
  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

  /// <inheritdoc/>
  public override void SetLength(long value) => throw new NotSupportedException();

  /// <inheritdoc/>
  public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
