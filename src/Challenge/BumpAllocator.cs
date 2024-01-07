using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public unsafe struct BumpAllocator(byte* buffer, int size)
{
    private int _offset;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Utf8Span Alloc(Span<byte> str)
    {
        var length = str.Length;
        //Debug.Assert(_offset + length < size);
        var offset = Interlocked.Add(ref _offset, length) - length;
        _offset += length;
        fixed (byte* pStr = str)
        {
            var start = buffer + offset;
            NativeMemory.Copy(pStr, start, (nuint)length);
            return new Utf8Span(start, length);
        }
    }
}
