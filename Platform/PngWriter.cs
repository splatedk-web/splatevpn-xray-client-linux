using System.Buffers.Binary;
using System.IO.Compression;
namespace SplateClientDesktop.Services;

internal static class PngWriter
{
    public static byte[] Gray8(int width,int height,byte[] pixels)
    {
        if(pixels.Length!=checked(width*height))throw new ArgumentException("Invalid PNG dimensions");
        using var result=new MemoryStream();result.Write(new byte[]{137,80,78,71,13,10,26,10});
        var header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(header,width);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height);header[8]=8;
        Chunk(result,"IHDR",header);
        using var packed=new MemoryStream();
        using(var zlib=new ZLibStream(packed,CompressionLevel.Optimal,true))
            for(var row=0;row<height;row++){zlib.WriteByte(0);zlib.Write(pixels,row*width,width);}
        Chunk(result,"IDAT",packed.ToArray());Chunk(result,"IEND",[]);return result.ToArray();
    }
    private static void Chunk(Stream output,string type,byte[] data)
    {
        Span<byte> number=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(number,data.Length);output.Write(number);
        var name=System.Text.Encoding.ASCII.GetBytes(type);output.Write(name);output.Write(data);
        uint crc=0xffffffff;
        foreach(var b in name.Concat(data)){crc^=b;for(var i=0;i<8;i++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}
        BinaryPrimitives.WriteUInt32BigEndian(number,~crc);output.Write(number);
    }
}
