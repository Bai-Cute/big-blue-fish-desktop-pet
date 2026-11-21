using System.Runtime.InteropServices;

namespace BigBlueFish.Windows;

// The 1280x720, 60 fps clips on macOS: VideoToolbox decodes the colour and alpha streams and alphacombine pairs them into
// AV12 frames (CompanionMediaStream); Accelerate's vImage turns each into the sprite's BGRA (straight alpha), about 3% of a
// core at 60 fps on an M4, where GStreamer's videoconvertscale needed a quarter of one.
// BT.601 with video range matches the CPU path (vp9alphadecodebin and videoconvertscale) within two levels on average.
internal static class MacVideoFrames
{
    const int Planar420Yp8CbCr8 = 4, Argb8888 = 0; // kvImage420Yp8_CbCr8, kvImageARGB8888 (vImage_Types.h)
    // kvImageDoNotTile: on the decoding thread itself. Split across GCD workers, each frame woke several threads and cost
    // several times the CPU time.
    const uint DoNotTile = 16;
    static readonly IntPtr conversion = Conversion();
    static readonly byte[] toBgra = [3, 2, 1, 0]; // ARGB channel for each BGRA byte
    // The CPU path drops alpha below 8 (LinuxVideoPlayer), so the faint fringe never blocks a click or shows a halo.
    static readonly byte[] alphaFloor = Enumerable.Range(0, 256).Select(v => (byte)(v < 8 ? 0 : v)).ToArray();

    // vImage_YpCbCrToARGB, which vImage wants 16-byte aligned; made once, kept for the process.
    static IntPtr Conversion()
    {
        var info = Marshal.AllocHGlobal(128 + 16);
        var aligned = (info + 15) & ~(nint)15;
        var matrix = new Matrix { Yp = 1, CrR = 1.402f, CrG = -0.7141363f, CbG = -0.3441363f, CbB = 1.772f }; // ITU-R BT.601-4
        var range = new PixelRange { YpBias = 16, CbCrBias = 128, YpRangeMax = 235, CbCrRangeMax = 240, YpMax = 235, YpMin = 16, CbCrMax = 240, CbCrMin = 16 };
        long error = vImageConvert_YpCbCrToARGB_GenerateConversion(ref matrix, ref range, aligned, Planar420Yp8CbCr8, Argb8888, 0);
        if (error != 0) throw new IOException("无法准备动画颜色转换（vImage " + error + "）。");
        return aligned;
    }

    // One AV12 frame into pixels (width x height BGRA, rows packed); alpha goes through scratch (width x height).
    internal static void ToBgra(CompanionMediaFrame frame, byte[] pixels, byte[] scratch, int width, int height)
    {
        if (pixels.Length < width * height * 4 || scratch.Length < width * height) throw new ArgumentException("buffers too small");
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned); var alpha = GCHandle.Alloc(scratch, GCHandleType.Pinned);
        try
        {
            var y = Plane(frame, 0, width, height); var cbcr = Plane(frame, 1, width / 2, height / 2); var a = Plane(frame, 2, width, height);
            var floor = new Buffer { Data = alpha.AddrOfPinnedObject(), Height = (nuint)height, Width = (nuint)width, RowBytes = (nuint)width };
            var bgra = new Buffer { Data = pinned.AddrOfPinnedObject(), Height = (nuint)height, Width = (nuint)width, RowBytes = (nuint)width * 4 };
            Check(vImageConvert_420Yp8_CbCr8ToARGB8888(ref y, ref cbcr, ref bgra, conversion, toBgra, 255, DoNotTile));
            Check(vImageTableLookUp_Planar8(ref a, ref floor, alphaFloor, DoNotTile));
            Check(vImageOverwriteChannels_ARGB8888(ref floor, ref bgra, ref bgra, 0x1, DoNotTile)); // 0x1: the last byte, BGRA's alpha
        }
        finally { pinned.Free(); alpha.Free(); }
    }

    static Buffer Plane(CompanionMediaFrame frame, int index, int width, int height) =>
        new() { Data = frame.Data + (nint)frame.Offsets[index], Height = (nuint)height, Width = (nuint)width, RowBytes = (nuint)frame.Strides[index] };
    static void Check(long error) { if (error != 0) throw new IOException("动画帧颜色转换失败（vImage " + error + "）。"); }

    [StructLayout(LayoutKind.Sequential)] struct Buffer { public IntPtr Data; public nuint Height, Width, RowBytes; }
    [StructLayout(LayoutKind.Sequential)] struct Matrix { public float Yp, CrR, CrG, CbG, CbB; }
    [StructLayout(LayoutKind.Sequential)] struct PixelRange { public int YpBias, CbCrBias, YpRangeMax, CbCrRangeMax, YpMax, YpMin, CbCrMax, CbCrMin; }
    const string Accelerate = "/System/Library/Frameworks/Accelerate.framework/Accelerate";
    [DllImport(Accelerate)] static extern long vImageConvert_YpCbCrToARGB_GenerateConversion(ref Matrix matrix, ref PixelRange range, IntPtr info, int inType, int outType, uint flags);
    [DllImport(Accelerate)] static extern long vImageConvert_420Yp8_CbCr8ToARGB8888(ref Buffer y, ref Buffer cbcr, ref Buffer destination, IntPtr info, byte[] permute, byte alpha, uint flags);
    [DllImport(Accelerate)] static extern long vImageTableLookUp_Planar8(ref Buffer source, ref Buffer destination, byte[] table, uint flags);
    [DllImport(Accelerate)] static extern long vImageOverwriteChannels_ARGB8888(ref Buffer newSource, ref Buffer original, ref Buffer destination, byte copyMask, uint flags);
}
