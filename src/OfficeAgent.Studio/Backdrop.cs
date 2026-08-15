using System.IO.Compression;

namespace OfficeAgent.Studio;

/// <summary>
/// Draws the backdrops the deck and the document sit on, as PNGs, in code.
/// </summary>
/// <remarks>
/// A demo that shipped a stock photograph would be demonstrating the photograph. Generating
/// the image here keeps the repository free of binary assets, makes the backdrop follow
/// <see cref="DesignSystem"/> like everything else, and means changing the accent changes
/// the cover too. The encoder is deliberately the smallest thing that produces a valid file:
/// 24-bit colour, no interlacing, one IDAT.
/// </remarks>
public static class Backdrop
{
    /// <summary>
    /// A diagonal wash between two colours, with a soft light at one corner. Big enough to
    /// scale to a slide or a page without banding, small enough to embed without thought.
    /// </summary>
    public static byte[] Gradient(string fromHex, string toHex, int width = 960, int height = 540)
    {
        var (r0, g0, b0) = Rgb(fromHex);
        var (r1, g1, b1) = Rgb(toHex);

        return Png(width, height, (x, y) =>
        {
            // Distance along the diagonal, eased so the middle transitions slowly and the
            // ends settle - a straight ramp reads as a printing error rather than a wash.
            var t = Ease((x + y) / 2.0);

            // A slight lift towards the top-left keeps a large flat area from looking dead.
            var lift = 0.10 * Math.Max(0, 1 - Math.Sqrt((x * x) + (y * y)));

            return (
                Mix(r0, r1, t, lift),
                Mix(g0, g1, t, lift),
                Mix(b0, b1, t, lift));
        });
    }

    /// <summary>
    /// A photographic backdrop: layered ridges under a warm sky, with haze, a vignette and
    /// film grain. Not a photograph, but it behaves like one - a wide tonal range, detail at
    /// every scale, and bright areas that will swallow dark text unless the opacity is
    /// brought down.
    /// </summary>
    /// <remarks>
    /// A flat gradient is a poor test of a background: text stays readable over it at almost
    /// any strength, so it never shows why <c>opacity</c> exists. This is here to make the
    /// difference between 100% and 20% obvious, and to give the demo one backdrop that is
    /// doing something a designer would recognise.
    /// <para>
    /// The whole thing is arithmetic on the pixel coordinates - value noise summed over
    /// several octaves - so it still ships no binary asset, and the same
    /// <paramref name="seed"/> always draws the same image.
    /// </para>
    /// </remarks>
    public static byte[] Photograph(
        string skyHex, string groundHex, int width = 1280, int height = 720, int seed = 7)
    {
        var (sr, sg, sb) = Rgb(skyHex);
        var (gr, gg, gb) = Rgb(groundHex);

        // Four ranges, each lower on the canvas, paler and flatter than the one in front of
        // it. Depth in a landscape is mostly haze, not detail.
        const int ranges = 4;

        return Png(width, height, (x, y) =>
        {
            // Sky: darkest at the top, warming towards the horizon.
            var sky = Ease(Math.Min(1, y * 1.6));
            double r = sr + ((gr - sr) * sky * 0.35);
            double g = sg + ((gg - sg) * sky * 0.35);
            double b = sb + ((gb - sb) * sky * 0.35);

            for (var i = 0; i < ranges; i++)
            {
                var depth = i / (double)(ranges - 1);          // 0 = furthest
                var horizon = 0.42 + (depth * 0.30);           // nearer ranges sit lower
                var relief = 0.10 - (depth * 0.045);           // and are drawn taller

                var ridge = horizon - (relief * Fbm(x * (2 + (i * 2.5)), i * 13.7 + seed, 4));
                if (y < ridge) continue;

                // How far down the slope this pixel is, for a little shading.
                var into = Math.Min(1, (y - ridge) / 0.55);
                var shade = 0.72 + (0.28 * into);

                // Haze mixes the far ranges back towards the sky.
                var haze = 0.55 * (1 - depth);
                var tone = 0.30 + (0.70 * depth);

                var rr = (gr * tone * shade) + (sr * haze);
                var gg2 = (gg * tone * shade) + (sg * haze);
                var bb = (gb * tone * shade) + (sb * haze);

                var blend = 1 - haze;
                r = (r * (1 - blend)) + (rr * blend);
                g = (g * (1 - blend)) + (gg2 * blend);
                b = (b * (1 - blend)) + (bb * blend);
            }

            // A vignette, then grain. Both are what stop a synthetic image reading as a
            // gradient with shapes on it.
            var dx = x - 0.5;
            var dy = y - 0.5;
            var vignette = 1 - (0.45 * Math.Min(1, ((dx * dx) + (dy * dy)) * 2.2));

            var grain = ((Noise(x * 640, y * 640, seed) - 0.5) * 10);

            return (
                Clamp((r * vignette) + grain),
                Clamp((g * vignette) + grain),
                Clamp((b * vignette) + grain));
        });
    }

    /// <summary>Value noise summed over octaves - detail at several scales at once.</summary>
    private static double Fbm(double x, double offset, int octaves)
    {
        double sum = 0, amplitude = 0.5, frequency = 1;
        for (var i = 0; i < octaves; i++)
        {
            sum += amplitude * Wave(x * frequency, offset + (i * 31.4));
            amplitude *= 0.5;
            frequency *= 2.1;
        }
        return sum;
    }

    /// <summary>One octave: smooth interpolation between pseudo-random values at integers.</summary>
    private static double Wave(double x, double offset)
    {
        var i = Math.Floor(x);
        var f = x - i;
        var a = Noise(i, offset, 0);
        var b = Noise(i + 1, offset, 0);
        return a + ((b - a) * Ease(f));
    }

    /// <summary>Deterministic hash noise in 0..1. No allocation, no shared state.</summary>
    private static double Noise(double x, double y, int seed)
    {
        var n = (int)(x * 374761393) + (int)(y * 668265263) + (seed * 1274126177);
        n = (n ^ (n >> 13)) * 1274126177;
        return ((n ^ (n >> 16)) & 0x7FFFFFF) / (double)0x7FFFFFF;
    }

    private static byte Clamp(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    private static double Ease(double t) => t * t * (3 - (2 * t));

    private static byte Mix(byte from, byte to, double t, double lift)
    {
        var value = (from * (1 - t)) + (to * t);
        value += (255 - value) * lift;
        return (byte)Math.Clamp(Math.Round(value), 0, 255);
    }

    private static (byte R, byte G, byte B) Rgb(string hex)
    {
        var value = hex.TrimStart('#');
        return (
            Convert.ToByte(value.Substring(0, 2), 16),
            Convert.ToByte(value.Substring(2, 2), 16),
            Convert.ToByte(value.Substring(4, 2), 16));
    }

    // ── the encoder ───────────────────────────────────────────────────────────

    /// <summary>
    /// Encodes a truecolour PNG. <paramref name="shade"/> is given coordinates from 0 to 1
    /// so the drawing above does not have to know the pixel size.
    /// </summary>
    private static byte[] Png(int width, int height, Func<double, double, (byte R, byte G, byte B)> shade)
    {
        // Each scanline is prefixed with its filter type; 0 means "store the bytes as they
        // are", which costs size but removes every chance of an encoding bug.
        var raw = new byte[height * ((width * 3) + 1)];
        var offset = 0;

        for (var y = 0; y < height; y++)
        {
            raw[offset++] = 0;
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = shade(x / (double)(width - 1), y / (double)(height - 1));
                raw[offset++] = r;
                raw[offset++] = g;
                raw[offset++] = b;
            }
        }

        using var file = new MemoryStream();
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);

        var header = new MemoryStream();
        WriteBigEndian(header, width);
        WriteBigEndian(header, height);
        header.WriteByte(8);    // bits per channel
        header.WriteByte(2);    // truecolour
        header.WriteByte(0);    // deflate
        header.WriteByte(0);    // adaptive filtering
        header.WriteByte(0);    // no interlacing
        WriteChunk(file, "IHDR", header.ToArray());

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(raw, 0, raw.Length);
        WriteChunk(file, "IDAT", compressed.ToArray());

        WriteChunk(file, "IEND", Array.Empty<byte>());
        return file.ToArray();
    }

    private static void WriteChunk(Stream file, string type, byte[] data)
    {
        WriteBigEndian(file, data.Length);

        var typeBytes = new[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
        file.Write(typeBytes, 0, 4);
        file.Write(data, 0, data.Length);

        // The CRC covers the type and the data, but not the length.
        var crc = Crc32(typeBytes, data);
        WriteBigEndian(file, unchecked((int)crc));
    }

    private static void WriteBigEndian(Stream file, int value)
    {
        file.WriteByte((byte)(value >> 24));
        file.WriteByte((byte)(value >> 16));
        file.WriteByte((byte)(value >> 8));
        file.WriteByte((byte)value);
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in first) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in second) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }
}
