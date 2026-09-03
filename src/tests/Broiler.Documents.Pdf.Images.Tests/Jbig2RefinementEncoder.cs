using System.Buffers.Binary;

namespace Broiler.Documents.Pdf.Images.Tests;

/// <summary>
/// Encodes a refinement so the decoder has something to be round-tripped
/// against.
/// </summary>
/// <remarks>
/// <para>
/// It walks the same templates the decoder does, which is the honest arrangement
/// and the limit of what the round trip proves: the two halves cannot disagree
/// about which neighbours form the context or in which order, so a template
/// misread from T.88's figures survives every test here. What the round trip does
/// exercise is everything else — the reference anchoring, the typical-prediction
/// rule, the adaptive pixels, and the arithmetic coder underneath.
/// </para>
/// <para>
/// Nothing here ships. This repository writes no JBIG2, and an encoder in the
/// product would be a separate decision with its own register row.
/// </para>
/// </remarks>
internal static class Jbig2RefinementEncoder
{
    internal static void Encode(
        MqEncoder encoder,
        MqContexts contexts,
        Jbig2Bitmap image,
        int template,
        bool typicalPrediction,
        Jbig2Bitmap reference,
        int referenceDx = 0,
        int referenceDy = 0,
        (int X, int Y)[]? adaptive = null)
    {
        ((int X, int Y)?[] codingTemplate, (int X, int Y)?[] referenceTemplate) =
            Jbig2RefinementDecoder.Templates(template);

        (int X, int Y)[] coding = Jbig2RefinementDecoder.Resolve(codingTemplate, adaptive ?? [], slot: 0);
        (int X, int Y)[] referenced = Jbig2RefinementDecoder.Resolve(referenceTemplate, adaptive ?? [], slot: 1);

        int typicalContext = template == 0 ? 0x0100 : 0x0080;
        bool predicting = false;

        for (int y = 0; y < image.Height; y++)
        {
            if (typicalPrediction)
            {
                // The flag may only be set for a row where the prediction would be
                // right about every pixel it settles — otherwise the decoder would
                // take a value the encoder never coded. Deciding that per row is
                // the encoder's whole job here.
                bool settled = RowIsSettled(image, y, reference, referenceDx, referenceDy);
                encoder.Encode(contexts, typicalContext, settled == predicting ? 0 : 1);
                predicting = settled;
            }

            for (int x = 0; x < image.Width; x++)
            {
                int referenceX = x - referenceDx;
                int referenceY = y - referenceDy;

                if (predicting && Jbig2RefinementDecoder.Settled(reference, referenceX, referenceY) is not null)
                    continue;

                int context = 0;
                foreach ((int dx, int dy) in coding)
                    context = (context << 1) | image.At(x + dx, y + dy);

                foreach ((int dx, int dy) in referenced)
                    context = (context << 1) | reference.At(referenceX + dx, referenceY + dy);

                encoder.Encode(contexts, context, image.Pixels[(y * image.Width) + x]);
            }
        }
    }

    /// <summary>
    /// Whether every pixel this row would skip already holds the value the
    /// reference settles it to.
    /// </summary>
    private static bool RowIsSettled(Jbig2Bitmap image, int y, Jbig2Bitmap reference, int referenceDx, int referenceDy)
    {
        for (int x = 0; x < image.Width; x++)
        {
            if (Jbig2RefinementDecoder.Settled(reference, x - referenceDx, y - referenceDy) is byte value &&
                image.Pixels[(y * image.Width) + x] != value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A refinement region segment body: a correction of whatever the page holds
    /// under it.
    /// </summary>
    internal static byte[] RegionSegment(
        Jbig2Bitmap refined,
        Jbig2Bitmap reference,
        int x = 0,
        int y = 0,
        int template = 0,
        bool typicalPrediction = false,
        int combination = 4,
        (int X, int Y)[]? adaptive = null)
    {
        var encoder = new MqEncoder();
        var contexts = new MqContexts(Jbig2RefinementDecoder.RefinementContextBits);
        Encode(encoder, contexts, refined, template, typicalPrediction, reference, adaptive: adaptive);

        var body = new List<byte>();
        Jbig2Streams.AddUInt32(body, refined.Width);
        Jbig2Streams.AddUInt32(body, refined.Height);
        Jbig2Streams.AddUInt32(body, x);
        Jbig2Streams.AddUInt32(body, y);
        body.Add((byte)combination);
        body.Add((byte)(template | (typicalPrediction ? 0x02 : 0x00)));

        if (template == 0)
        {
            foreach ((int ax, int ay) in adaptive ?? [(-1, -1), (-1, -1)])
            {
                body.Add((byte)(sbyte)ax);
                body.Add((byte)(sbyte)ay);
            }
        }

        body.AddRange(encoder.Flush());
        return [.. body];
    }
}
