using System;
using System.IO;
using System.IO.Compression;

namespace BigBlueFish.Windows;

internal sealed class CompanionAnimationMask
{
    internal readonly int Width,Height,Rate,Count;
    readonly byte[] bits,pixels;
    internal CompanionAnimationMask(string file)
    {
        using var reader=new BinaryReader(File.OpenRead(Path.ChangeExtension(file,".mask")));
        if(reader.ReadUInt32()!=0x4d464242||reader.ReadUInt32()!=1)throw new IOException("动画点击区域格式不正确。");
        Width=reader.ReadInt32();Height=reader.ReadInt32();Rate=reader.ReadInt32();Count=reader.ReadInt32();reader.ReadBytes(32);
        if(Width!=320||Height!=180||Rate!=20||Count<1||Count>12000)throw new IOException("动画点击区域尺寸不正确。");
        bits=new byte[checked(Width*Height/8*Count)];pixels=new byte[Width*Height*4];
        using var compressed=new ZLibStream(reader.BaseStream,CompressionMode.Decompress);compressed.ReadExactly(bits);
        if(compressed.ReadByte()!=-1)throw new IOException("动画点击区域长度不正确。");
    }
    internal byte[] At(double seconds)
    {
        int offset=Math.Clamp((int)(seconds*Rate),0,Count-1)*(Width*Height/8);
        for(int i=0;i<Width*Height;i++)pixels[i*4+3]=(bits[offset+i/8]&(1<<(i%8)))!=0?(byte)255:(byte)0;
        return pixels;
    }
}
