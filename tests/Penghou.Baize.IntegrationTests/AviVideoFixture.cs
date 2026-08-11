using System.Text;

namespace Penghou.Baize.IntegrationTests;

internal static class AviVideoFixture
{
    private const int Width = 64;
    private const int Height = 64;
    private const int FramesPerSecond = 2;
    private const int FramesPerColor = 4;
    private const int FrameCount = FramesPerColor * 3;
    private const int RowSize = ((Width * 3) + 3) & ~3;
    private const int FrameSize = RowSize * Height;

    public static byte[] CreateRedGreenBlueSequence()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        WriteFourCc(writer, "RIFF");
        var riffSizeOffset = ReserveSize(writer);
        WriteFourCc(writer, "AVI ");

        WriteHeaderList(writer);
        WriteFrameList(writer);
        PatchSize(stream, riffSizeOffset);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteHeaderList(BinaryWriter writer)
    {
        WriteFourCc(writer, "LIST");
        var headerListSizeOffset = ReserveSize(writer);
        WriteFourCc(writer, "hdrl");

        WriteChunk(writer, "avih", chunk =>
        {
            chunk.Write(1_000_000 / FramesPerSecond);
            chunk.Write(FrameSize * FramesPerSecond);
            chunk.Write(0);
            chunk.Write(0);
            chunk.Write(FrameCount);
            chunk.Write(0);
            chunk.Write(1);
            chunk.Write(FrameSize);
            chunk.Write(Width);
            chunk.Write(Height);
            chunk.Write(0);
            chunk.Write(0);
            chunk.Write(0);
            chunk.Write(0);
        });

        WriteFourCc(writer, "LIST");
        var streamListSizeOffset = ReserveSize(writer);
        WriteFourCc(writer, "strl");
        WriteChunk(writer, "strh", chunk =>
        {
            WriteFourCc(chunk, "vids");
            WriteFourCc(chunk, "DIB ");
            chunk.Write(0);
            chunk.Write((short)0);
            chunk.Write((short)0);
            chunk.Write(0);
            chunk.Write(1);
            chunk.Write(FramesPerSecond);
            chunk.Write(0);
            chunk.Write(FrameCount);
            chunk.Write(FrameSize);
            chunk.Write(-1);
            chunk.Write(0);
            chunk.Write((short)0);
            chunk.Write((short)0);
            chunk.Write((short)Width);
            chunk.Write((short)Height);
        });
        WriteChunk(writer, "strf", chunk =>
        {
            chunk.Write(40);
            chunk.Write(Width);
            chunk.Write(Height);
            chunk.Write((short)1);
            chunk.Write((short)24);
            chunk.Write(0);
            chunk.Write(FrameSize);
            chunk.Write(0);
            chunk.Write(0);
            chunk.Write(0);
            chunk.Write(0);
        });
        PatchSize(writer.BaseStream, streamListSizeOffset);
        PatchSize(writer.BaseStream, headerListSizeOffset);
    }

    private static void WriteFrameList(BinaryWriter writer)
    {
        WriteFourCc(writer, "LIST");
        var frameListSizeOffset = ReserveSize(writer);
        WriteFourCc(writer, "movi");

        WriteColorFrames(writer, blue: 0, green: 0, red: 255);
        WriteColorFrames(writer, blue: 0, green: 255, red: 0);
        WriteColorFrames(writer, blue: 255, green: 0, red: 0);
        PatchSize(writer.BaseStream, frameListSizeOffset);
    }

    private static void WriteColorFrames(
        BinaryWriter writer,
        byte blue,
        byte green,
        byte red)
    {
        for (var frame = 0; frame < FramesPerColor; frame++)
        {
            WriteChunk(writer, "00db", chunk =>
            {
                for (var pixel = 0; pixel < Width * Height; pixel++)
                {
                    chunk.Write(blue);
                    chunk.Write(green);
                    chunk.Write(red);
                }
            });
        }
    }

    private static void WriteChunk(
        BinaryWriter writer,
        string id,
        Action<BinaryWriter> writeBody)
    {
        WriteFourCc(writer, id);
        var sizeOffset = ReserveSize(writer);
        writeBody(writer);
        var bodySize = checked((int)(writer.BaseStream.Position - sizeOffset - 4));
        if ((bodySize & 1) != 0)
            writer.Write((byte)0);
        PatchSize(writer.BaseStream, sizeOffset, bodySize);
    }

    private static long ReserveSize(BinaryWriter writer)
    {
        var offset = writer.BaseStream.Position;
        writer.Write(0);
        return offset;
    }

    private static void PatchSize(Stream stream, long sizeOffset) =>
        PatchSize(
            stream,
            sizeOffset,
            checked((int)(stream.Position - sizeOffset - 4)));

    private static void PatchSize(Stream stream, long sizeOffset, int size)
    {
        var end = stream.Position;
        stream.Position = sizeOffset;
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BitConverter.TryWriteBytes(bytes, size);
        stream.Write(bytes);
        stream.Position = end;
    }

    private static void WriteFourCc(BinaryWriter writer, string value)
    {
        if (value.Length != 4)
            throw new ArgumentException("A FourCC must contain four characters.", nameof(value));
        writer.Write(Encoding.ASCII.GetBytes(value));
    }
}
