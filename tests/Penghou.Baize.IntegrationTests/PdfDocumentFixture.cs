using System.Globalization;
using System.Text;

namespace Penghou.Baize.IntegrationTests;

internal static class PdfDocumentFixture
{
    public static byte[] Create()
    {
        using var stream = new MemoryStream();
        Write(stream, "%PDF-1.4\n");
        var offsets = new long[6];

        WriteObject(stream, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(stream, offsets, 2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObject(
            stream,
            offsets,
            3,
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
            "/Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>");
        WriteObject(
            stream,
            offsets,
            4,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        const string pageContent =
            "BT\n" +
            "/F1 18 Tf\n" +
            "72 720 Td\n" +
            "(BAIZE LIVE DOCUMENT) Tj\n" +
            "0 -32 Td\n" +
            "(Reference: ORBIT-417) Tj\n" +
            "0 -32 Td\n" +
            "(Quantities: 8 and 13) Tj\n" +
            "ET\n";
        offsets[5] = stream.Position;
        Write(
            stream,
            "5 0 obj\n<< /Length " +
            Encoding.ASCII.GetByteCount(pageContent).ToString(CultureInfo.InvariantCulture) +
            " >>\nstream\n");
        Write(stream, pageContent);
        Write(stream, "endstream\nendobj\n");

        var crossReferenceOffset = stream.Position;
        Write(stream, "xref\n0 6\n0000000000 65535 f \n");
        for (var objectNumber = 1; objectNumber < offsets.Length; objectNumber++)
        {
            Write(
                stream,
                offsets[objectNumber].ToString("0000000000", CultureInfo.InvariantCulture) +
                " 00000 n \n");
        }

        Write(
            stream,
            "trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n" +
            crossReferenceOffset.ToString(CultureInfo.InvariantCulture) +
            "\n%%EOF\n");
        return stream.ToArray();
    }

    private static void WriteObject(
        Stream stream,
        long[] offsets,
        int objectNumber,
        string body)
    {
        offsets[objectNumber] = stream.Position;
        Write(
            stream,
            objectNumber.ToString(CultureInfo.InvariantCulture) +
            " 0 obj\n" + body + "\nendobj\n");
    }

    private static void Write(Stream stream, string value) =>
        stream.Write(Encoding.ASCII.GetBytes(value));
}
