using System.Buffers;
using System.Runtime.InteropServices;
using Imazen.Routing.Requests;
using Microsoft.Extensions.Primitives;

namespace Imazen.Routing.HttpAbstractions;

public static class BufferWriterExtensions
{



    public static void WriteWtf16String(this IBufferWriter<byte> writer, StringValues stringValues, char delimiter = ',')
    {
        switch (stringValues.Count)
        {
            case 0:
                writer.WriteWtf16String("");
                return;
            case 1:
                writer.WriteWtf16String(stringValues.ToString());
                return;
            case > 1:
            {
                var arr = stringValues.ToArray();
                foreach (var str in arr)
                {
                    writer.WriteWtf16String(str);
                    if (arr[^1] != str)
                        writer.WriteWtf16Char(delimiter);
                }

                break;
            }
        }
    }
    
    
    public static void WriteWtf16String(this IBufferWriter<byte> writer, string? str)
    {
        if (str == null)
        {
            writer.WriteWtf16String("(null)");
            return;
        }
        var byteCount = str.Length * 2;
        var buffer = writer.GetSpan(byteCount);
        // marshal
        ReadOnlySpan<byte> byteSpan = MemoryMarshal.AsBytes(str.AsSpan());
        if (byteSpan.Length > buffer.Length) throw new InvalidOperationException("Buffer length mismatch");
        byteSpan.CopyTo(buffer);
        writer.Advance(byteCount);
    }
    // Write single char
    public static void WriteWtf16Char(this IBufferWriter<byte> writer, char c)
    {
        var buffer = writer.GetSpan(2);
        buffer[0] = (byte) c;
        buffer[1] = (byte) (c >> 8);
        writer.Advance(2);
    }
    
    // Write byte
    public static void WriteByte(this IBufferWriter<byte> writer, byte b)
    {
        var buffer = writer.GetSpan(1);
        buffer[0] = b;
        writer.Advance(1);
    }
    
    // Write span<byte>
    public static void WriteBytes(this IBufferWriter<byte> writer, ReadOnlySpan<byte> bytes)
    {
        var buffer = writer.GetSpan(bytes.Length);
        bytes.CopyTo(buffer);
        writer.Advance(bytes.Length);
    }
    // Write float
    public static void WriteFloat(this IBufferWriter<byte> writer, float f)
    {
        WriteAsInMemory(writer, f);
    }
    public static void WriteAsInMemory<T>(this IBufferWriter<byte> writer, T value) where T : unmanaged
    {
        // MemoryMarshal.CreateSpan is only on .net2.1+
#if NETSTANDARD2_1_OR_GREATER
        writer.WriteBytes(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref f, 1)));
#else
        // stackalloc a float span otherwise
        Span<T> values = stackalloc T[1];
        values[0] = value;
        writer.WriteBytes(MemoryMarshal.AsBytes(values));
#endif
    }
    public static void WriteAsInMemory<T>(this IBufferWriter<byte> writer, Span<T> values) where T : unmanaged
    {
        writer.WriteBytes(MemoryMarshal.AsBytes(values));
    }
    public static void WriteLong(this IBufferWriter<byte> writer, long l)
    {
        var buffer = writer.GetSpan(8);
        buffer[0] = (byte) l;
        buffer[1] = (byte) (l >> 8);
        buffer[2] = (byte) (l >> 16);
        buffer[3] = (byte) (l >> 24);
        buffer[4] = (byte) (l >> 32);
        buffer[5] = (byte) (l >> 40);
        buffer[6] = (byte) (l >> 48);
        buffer[7] = (byte) (l >> 56);
        writer.Advance(8);
    }
    
    public static void WriteInt(this IBufferWriter<byte> writer, int i)
    {
        var buffer = writer.GetSpan(4);
        buffer[0] = (byte) i;
        buffer[1] = (byte) (i >> 8);
        buffer[2] = (byte) (i >> 16);
        buffer[3] = (byte) (i >> 24);
        writer.Advance(4);
    }
    
    public static void WriteWtf16Dictionary(this IBufferWriter<byte> writer, IDictionary<string, string>? dict)
    {
        if (dict == null)
        {
            writer.WriteWtf16String("(null)");
            return;
        }
        //serialize into {key=value,key2=value2,} syntax
        writer.WriteWtf16Char('{');
        foreach (var pair in dict)
        {
            writer.WriteWtf16String(pair.Key);
            writer.WriteWtf16Char('=');
            writer.WriteWtf16String(pair.Value);
            writer.WriteWtf16Char(',');
        }
    }
    public static void WriteWtf16Dictionary(this IBufferWriter<byte> writer, IDictionary<string, StringValues>? dict)
    {
        if (dict == null)
        {
            writer.WriteWtf16String("(null)");
            return;
        }
        //serialize into {key=value,key2=value2,} syntax
        writer.WriteWtf16Char('{');
        foreach (var pair in dict)
        {
            writer.WriteWtf16String(pair.Key);
            writer.WriteWtf16Char('=');
            writer.WriteWtf16String(pair.Value);
            writer.WriteWtf16Char(',');
        }
    }
    
    // Write IGetRequestSnapshot
    public static void WriteWtf16PathAndRouteValuesCacheKeyBasis(this IBufferWriter<byte> writer, IGetResourceSnapshot request)
    {
        //path = request.Path
        //route values = request.RouteValues

        writer.WriteWtf16String("\npath=");
        writer.WriteWtf16String(request.Path);
        writer.WriteWtf16String("\nrouteValues=");
        writer.WriteWtf16Dictionary(request.ExtractedData);
        
    }
    
    public static void WriteUtf8String(this IBufferWriter<byte> writer, StringValues str)
    {
        WriteUtf8String(writer, str.ToString()); // May allocate with multiple values
    }
    public static void WriteUtf8String(this IBufferWriter<byte> writer, string str)
    {
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(str);
        var span = writer.GetSpan(byteCount);
#if NET5_0_OR_GREATER
        var written = System.Text.Encoding.UTF8.GetBytes(str.AsSpan(), span);
        writer.Advance(written);
#else
        var writtenBytes = System.Text.Encoding.UTF8.GetBytes(str);
        if (writtenBytes.Length != span.Length) throw new InvalidOperationException("Buffer length mismatch");
        writtenBytes.CopyTo(span);
        writer.Advance(byteCount);
#endif
    }
    
    // we want overloads for float, double, int, unit, long, ulong, bool, char, string, byte, enum
    // all nullable and non-nullable (we write (char?)null as 0)
    public static void WriteWtf(this IBufferWriter<byte> writer, string str)
        => WriteWtf16String(writer, str);
    
    public static void WriteWtf(this IBufferWriter<byte> writer, char c)
        => WriteWtf16Char(writer, c);
    
    public static void WriteWtf(this IBufferWriter<byte> writer, bool b)
        => WriteByte(writer, (byte)(b ? 1 : 0));

    public static void WriteWtf(this IBufferWriter<byte> writer, int i)
        => WriteInt(writer, i);
    
    public static void WriteWtf(this IBufferWriter<byte> writer, uint i)
        => WriteInt(writer, (int)i);
    
    public static void WriteWtf(this IBufferWriter<byte> writer, long l)
        => WriteLong(writer, l);
    
    public static void WriteWtf(this IBufferWriter<byte> writer, ulong l)
        => WriteLong(writer, (long)l);
    
    public static void WriteWtf(this IBufferWriter<byte> writer, float f)
    {
        WriteFloat(writer, f);
    }
    
    public static void WriteWtf(this IBufferWriter<byte> writer, double d)
    {
        WriteAsInMemory(writer, d);
    }
    
    public static void WriteWtf(this IBufferWriter<byte> writer, byte b)
    {
        WriteByte(writer, b);
    }
    
    public static void WriteWtf<T>(this IBufferWriter<byte> writer, T b) where T : unmanaged
    {
        WriteAsInMemory(writer, b);
    }
    
    public static void WriteWtf<T>(this IBufferWriter<byte> writer, T? b) where T : unmanaged
    {
        if (b == null)
        {
            WriteWtf16String(writer, "(null)");
            return;
        }
        WriteAsInMemory(writer, b.Value);
    }
    
    // now nullable overloads that write a distinct value for null that can't be confused with a non-null value
    // So we use extra bytes or fewer bytes, but never the same number
    
    
    
        
    
}