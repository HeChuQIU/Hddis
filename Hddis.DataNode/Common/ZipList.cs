using System.Buffers;
using System.Buffers.Binary;
using System.Collections;
using System.Runtime.CompilerServices;

namespace Hddis.DataNode.Common;

public class ZipList : IEnumerable<byte[]>, IDisposable
{
    private byte[] _buffer;
    private int _length;
    private int _capacity;
    private const byte ZipEnd = 0xFF;
    private const int InitialCapacity = 128;
    private const int MaxEntrySize = 64; // 适合小数据存储

    // ZipList 头部字段的偏移量
    private const int ZlbytesOffset = 0;
    private const int ZltailOffset = 4;
    private const int ZllenOffset = 8;
    private const int HeaderSize = 11; // zlbytes(4) + zltail(4) + zllen(2) + zlend(1)

    public ZipList()
    {
        _capacity = InitialCapacity;
        _buffer = ArrayPool<byte>.Shared.Rent(_capacity);
        _length = HeaderSize - 1; // 减去 zlend，因为它将在首次添加时添加

        // 初始化头部
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(ZlbytesOffset), _capacity);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(ZltailOffset), 0);
        BinaryPrimitives.WriteInt16LittleEndian(_buffer.AsSpan(ZllenOffset), 0);
        _buffer[_length] = ZipEnd; // 设置结束标记
        _length++;
    }

    public int Count => BinaryPrimitives.ReadInt16LittleEndian(_buffer.AsSpan(ZllenOffset));

    public int Length => BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(ZlbytesOffset));

    // 添加元素到末尾
    public void Push(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > MaxEntrySize)
            throw new ArgumentException($"Data too large for ZipList. Max size: {MaxEntrySize}");

        var entryLength = CalculateEntryLength(data);
        EnsureCapacity(entryLength);

        var prevOffset = BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(ZltailOffset));
        var entryOffset = prevOffset > 0 ? prevOffset + GetEntryLength(prevOffset) : HeaderSize - 1;

        // 写入新节点
        WriteEntry(entryOffset, data, prevOffset > 0 ? GetEntryLength(prevOffset) : 0);

        // 更新尾部偏移量和长度
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(ZltailOffset), entryOffset);
        var newCount = (short)(Count + 1);
        BinaryPrimitives.WriteInt16LittleEndian(_buffer.AsSpan(ZllenOffset), newCount);

        // 更新总字节数
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(ZlbytesOffset), _length);
    }

    // 获取指定位置的元素
    public byte[] Get(int index)
    {
        if (index < 0 || index >= Count)
            throw new IndexOutOfRangeException();

        var offset = FindEntryOffset(index);
        return ReadEntry(offset);
    }

    // 删除指定位置的元素
    public void Delete(int index)
    {
        if (index < 0 || index >= Count)
            throw new IndexOutOfRangeException();

        var offset = FindEntryOffset(index);
        var entryLength = GetEntryLength(offset);
        var nextOffset = offset + entryLength;

        // 移动后续数据
        var bytesToMove = _length - nextOffset;
        if (bytesToMove > 0)
        {
            var source = _buffer.AsSpan(nextOffset, bytesToMove);
            var destination = _buffer.AsSpan(offset, bytesToMove);
            source.CopyTo(destination);
        }

        _length -= entryLength;

        // 更新计数和总长度
        var newCount = (short)(Count - 1);
        BinaryPrimitives.WriteInt16LittleEndian(_buffer.AsSpan(ZllenOffset), newCount);
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(ZlbytesOffset), _length);

        // 如果删除的是最后一个元素，更新尾部偏移量
        if (index == newCount)
        {
            var newTailOffset = index > 0 ? FindEntryOffset(index - 1) : 0;
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(ZltailOffset), newTailOffset);
        }

        // 更新后续节点的 prevlen 字段（可能触发连锁更新）
        if (index < newCount)
        {
            UpdatePrevlenForSubsequentEntries(offset);
        }
    }

    // 实现枚举器
    public IEnumerator<byte[]> GetEnumerator()
    {
        var count = Count;
        for (var i = 0; i < count; i++)
        {
            yield return Get(i);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private bool _disposed = false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing && _buffer != null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
            _length = 0;
            _capacity = 0;
        }

        _disposed = true;
    }

    #region 私有辅助方法

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CalculateEntryLength(byte[] data)
    {
        // prevlen: 1或5字节
        const int prevlenSize = 1; // 默认为1字节

        // encoding: 1、2或5字节
        var encodingSize = GetEncodingSize(data);

        // 数据长度
        var dataSize = data.Length;

        return prevlenSize + encodingSize + dataSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetEncodingSize(byte[] data)
    {
        return data.Length switch
        {
            // 简化实现：总是使用字符串编码
            < 64 => 1,
            < 16384 => 2,
            _ => 5
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteEncoding(Span<byte> span, int dataLength)
    {
        switch (dataLength)
        {
            case < 64:
                span[0] = (byte)(0xC0 | dataLength);
                break;
            case < 16384:
                span[0] = (byte)(0x80 | (dataLength >> 8));
                span[1] = (byte)dataLength;
                break;
            default:
                span[0] = 0x80;
                BinaryPrimitives.WriteInt32LittleEndian(span[1..], dataLength);
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteEntry(int offset, byte[] data, int prevEntryLength)
    {
        var span = _buffer.AsSpan(offset);
        var spanIndex = 0;

        // 写入 prevlen
        if (prevEntryLength < 254)
        {
            span[spanIndex++] = (byte)prevEntryLength;
        }
        else
        {
            span[spanIndex++] = 0xFE;
            BinaryPrimitives.WriteInt32LittleEndian(span[spanIndex..], prevEntryLength);
            spanIndex += 4;
        }

        // 写入 encoding
        var encodingSize = GetEncodingSize(data);
        WriteEncoding(span[spanIndex..], data.Length);
        spanIndex += encodingSize;

        // 写入数据
        data.AsSpan().CopyTo(span[spanIndex..]);
        spanIndex += data.Length;

        // 移除旧的 ZIP_END，添加新的
        if (_buffer[_length - 1] == ZipEnd)
        {
            _length--; // 移除旧的结束标记
        }

        // 更新长度
        _length += spanIndex;

        // 添加新的结束标记
        _buffer[_length - 1] = ZipEnd;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte[] ReadEntry(int offset)
    {
        var span = _buffer.AsSpan(offset);
        var spanIndex = 0;

        // 读取 prevlen
        var prevlenByte = span[spanIndex++];
        int prevlen = prevlenByte;
        if (prevlenByte == 0xFE)
        {
            prevlen = BinaryPrimitives.ReadInt32LittleEndian(span[spanIndex..]);
            spanIndex += 4;
        }

        // 读取 encoding
        var encodingByte = span[spanIndex++];
        int dataLength;

        if ((encodingByte & 0xC0) == 0xC0) // 11000000
        {
            // 1字节编码
            dataLength = encodingByte & 0x3F;
        }
        else if ((encodingByte & 0x80) == 0x80) // 10000000
        {
            // 2字节或5字节编码
            if (encodingByte == 0x80)
            {
                // 5字节编码
                dataLength = BinaryPrimitives.ReadInt32LittleEndian(span[spanIndex..]);
                spanIndex += 4;
            }
            else
            {
                // 2字节编码
                dataLength = ((encodingByte & 0x3F) << 8) | span[spanIndex++];
            }
        }
        else
        {
            // 无效编码
            throw new FormatException("Invalid encoding");
        }

        // 读取数据
        var result = new byte[dataLength];
        span.Slice(spanIndex, dataLength).CopyTo(result);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetEntryLength(int offset)
    {
        var span = _buffer.AsSpan(offset);
        var spanIndex = 0;

        // 读取 prevlen
        var prevlenByte = span[spanIndex++];
        if (prevlenByte == 0xFE)
        {
            spanIndex += 4;
        }

        // 读取 encoding
        var encodingByte = span[spanIndex++];
        int dataLength;

        if ((encodingByte & 0xC0) == 0xC0)
        {
            dataLength = encodingByte & 0x3F;
        }
        else if ((encodingByte & 0x80) == 0x80)
        {
            if (encodingByte == 0x80)
            {
                dataLength = BinaryPrimitives.ReadInt32LittleEndian(span[spanIndex..]);
                spanIndex += 4;
            }
            else
            {
                dataLength = ((encodingByte & 0x3F) << 8) | span[spanIndex++];
            }
        }
        else
        {
            throw new FormatException("Invalid encoding");
        }

        return spanIndex + dataLength;
    }

    private int FindEntryOffset(int index)
    {
        var offset = HeaderSize - 1; // 从第一个entry开始
        for (var i = 0; i < index; i++)
        {
            offset += GetEntryLength(offset);
        }

        return offset;
    }

    private void EnsureCapacity(int additionalLength)
    {
        if (_length + additionalLength > _capacity)
        {
            var newCapacity = Math.Max(_capacity * 2, _length + additionalLength);
            var newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);

            _buffer.AsSpan(0, _length).CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(_buffer);

            _buffer = newBuffer;
            _capacity = newCapacity;

            // 更新zlbytes
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(ZlbytesOffset), _capacity);
        }
    }

    private void UpdatePrevlenForSubsequentEntries(int startOffset)
    {
        var offset = startOffset;
        var nextOffset = offset + GetEntryLength(offset);

        while (nextOffset < _length - 1) // 减去zlend
        {
            var entryLength = GetEntryLength(offset);
            var nextEntryLength = GetEntryLength(nextOffset);

            // 检查是否需要更新prevlen
            var nextEntry = _buffer.AsSpan(nextOffset);
            var currentPrevlen = nextEntry[0];
            var requiredPrevlenSize = entryLength < 254 ? 1 : 5;
            var currentPrevlenSize = currentPrevlen < 254 ? 1 : 5;

            if (requiredPrevlenSize != currentPrevlenSize)
            {
                // 需要重新编码节点
                var data = ReadEntry(nextOffset);
                var newEntryLength = CalculateEntryLength(data);

                if (requiredPrevlenSize > currentPrevlenSize)
                {
                    // 需要更多空间，移动后续数据
                    EnsureCapacity(newEntryLength - nextEntryLength);
                }

                // 重新写入节点
                var prevEntryLength = offset == 0 ? 0 : GetEntryLength(offset);
                WriteEntry(nextOffset, data, prevEntryLength);
            }
            else
            {
                // 只需更新prevlen字段
                if (entryLength < 254)
                {
                    nextEntry[0] = (byte)entryLength;
                }
                else
                {
                    nextEntry[0] = 0xFE;
                    BinaryPrimitives.WriteInt32LittleEndian(nextEntry[1..], entryLength);
                }
            }

            offset = nextOffset;
            nextOffset += GetEntryLength(nextOffset);
        }
    }

    #endregion
}