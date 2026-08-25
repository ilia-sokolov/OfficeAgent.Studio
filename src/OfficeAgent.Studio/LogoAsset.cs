using System.IO.Compression;
using OfficeAgent.Abstractions;

namespace OfficeAgent.Studio;

/// <summary>A validated, transparent PNG supplied by the operator for this run.</summary>
public sealed class LogoAsset
{
    private const int MaximumFileBytes = 5 * 1024 * 1024;
    private const int MaximumDimension = 4096;
    private const long MaximumPixels = 16_000_000;

    private LogoAsset(byte[] bytes, LogoRaster raster, string altText)
    {
        Bytes = bytes;
        Raster = raster;
        AltText = altText;
    }

    /// <summary>The original PNG, retained for lossless insertion into Word.</summary>
    public byte[] Bytes { get; }

    /// <summary>Accessible alternative text used by Office image operations.</summary>
    public string AltText { get; }

    internal LogoRaster Raster { get; }

    internal static LogoAsset Load(string path, string altText)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("The logo file path is empty.");

        var fullPath = Path.GetFullPath(path.Trim());
        try
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists) throw new FileNotFoundException("The logo file does not exist.", fullPath);
            if (info.Length == 0 || info.Length > MaximumFileBytes)
                throw new InvalidDataException("The logo must be between 1 byte and 5 MiB.");

            var bytes = File.ReadAllBytes(fullPath);
            var raster = LogoPng.Decode(bytes, MaximumDimension, MaximumPixels);
            return new LogoAsset(bytes, raster, altText);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new ArgumentException(
                $"Could not load logo '{fullPath}': {error.Message} " +
                "Use a non-interlaced PNG (RGB, RGBA, grayscale, or indexed colour).",
                error);
        }
    }

    internal (int Width, int Height) Fit(int maximumWidth, int maximumHeight)
    {
        var scale = Math.Min(
            maximumWidth / (double)Raster.Width,
            maximumHeight / (double)Raster.Height);
        scale = Math.Min(1, scale);
        return (
            Math.Max(1, (int)Math.Round(Raster.Width * scale)),
            Math.Max(1, (int)Math.Round(Raster.Height * scale)));
    }

    internal InsertImageOp InsertBefore(string paraId, int maximumWidth, int maximumHeight)
    {
        var (width, height) = Fit(maximumWidth, maximumHeight);
        return new InsertImageOp
        {
            Target = new TextSpanAnchor { ParaId = paraId, Expect = string.Empty },
            Position = InsertPosition.Before,
            Base64Bytes = Convert.ToBase64String(Bytes),
            ImageType = "png",
            WidthPx = width,
            HeightPx = height,
            AltText = AltText
        };
    }
}

internal sealed record LogoRaster(int Width, int Height, byte[] Rgba);

/// <summary>
/// A bounded PNG reader for logo assets. It handles the ordinary web-logo variants without
/// adding a native graphics dependency to a document-generation CLI.
/// </summary>
internal static class LogoPng
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    internal static LogoRaster Decode(byte[] file, int maximumDimension, long maximumPixels)
    {
        if (file.Length < Signature.Length || !file.AsSpan(0, Signature.Length).SequenceEqual(Signature))
            throw new InvalidDataException("The file is not a PNG.");

        var offset = Signature.Length;
        int width = 0, height = 0, bitDepth = 0, colorType = -1, interlace = -1;
        byte[]? palette = null;
        byte[]? transparency = null;
        using var compressed = new MemoryStream();
        var sawHeader = false;

        while (offset + 12 <= file.Length)
        {
            var length = ReadInt(file, offset);
            offset += 4;
            if (length < 0 || offset + 8L + length > file.Length)
                throw new InvalidDataException("The PNG contains a truncated chunk.");

            var type = System.Text.Encoding.ASCII.GetString(file, offset, 4);
            offset += 4;
            var data = file.AsSpan(offset, length);

            switch (type)
            {
                case "IHDR":
                    if (sawHeader || length != 13)
                        throw new InvalidDataException("The PNG has an invalid header.");
                    width = ReadInt(file, offset);
                    height = ReadInt(file, offset + 4);
                    bitDepth = file[offset + 8];
                    colorType = file[offset + 9];
                    interlace = file[offset + 12];
                    sawHeader = true;
                    break;
                case "PLTE":
                    palette = data.ToArray();
                    break;
                case "tRNS":
                    transparency = data.ToArray();
                    break;
                case "IDAT":
                    compressed.Write(data);
                    break;
                case "IEND":
                    offset = file.Length;
                    continue;
            }

            offset += length + 4; // data and CRC
        }

        if (!sawHeader || width <= 0 || height <= 0)
            throw new InvalidDataException("The PNG has no valid dimensions.");
        if (width > maximumDimension || height > maximumDimension || (long)width * height > maximumPixels)
            throw new InvalidDataException(
                $"The logo is {width}x{height}; maximum is {maximumDimension}px per side and {maximumPixels:N0} pixels.");
        if (interlace != 0)
            throw new InvalidDataException("Interlaced PNGs are not supported.");

        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new InvalidDataException($"PNG colour type {colorType} is not supported.")
        };
        if (colorType == 3 ? bitDepth is not (1 or 2 or 4 or 8) : bitDepth != 8)
            throw new InvalidDataException($"PNG bit depth {bitDepth} is not supported for colour type {colorType}.");
        if (colorType == 3 && (palette is null || palette.Length == 0 || palette.Length % 3 != 0))
            throw new InvalidDataException("The indexed PNG has no valid palette.");

        var rowBytes = checked((width * channels * bitDepth + 7) / 8);
        var expected = checked(height * (rowBytes + 1));
        var filtered = new byte[expected];
        compressed.Position = 0;
        using (var inflater = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
        {
            var read = 0;
            while (read < filtered.Length)
            {
                var count = inflater.Read(filtered, read, filtered.Length - read);
                if (count == 0) break;
                read += count;
            }
            if (read != filtered.Length || inflater.ReadByte() != -1)
                throw new InvalidDataException("The PNG pixel stream has an unexpected length.");
        }

        var scan = Unfilter(filtered, height, rowBytes, Math.Max(1, (channels * bitDepth + 7) / 8));
        return new LogoRaster(width, height, ToRgba(scan, width, height, bitDepth, colorType, palette, transparency));
    }

    private static byte[] Unfilter(byte[] filtered, int height, int rowBytes, int bytesPerPixel)
    {
        var result = new byte[checked(height * rowBytes)];
        var source = 0;
        for (var y = 0; y < height; y++)
        {
            var filter = filtered[source++];
            if (filter > 4) throw new InvalidDataException($"The PNG uses unknown row filter {filter}.");
            var row = y * rowBytes;
            var prior = row - rowBytes;
            for (var x = 0; x < rowBytes; x++)
            {
                var raw = filtered[source++];
                var left = x >= bytesPerPixel ? result[row + x - bytesPerPixel] : 0;
                var above = y > 0 ? result[prior + x] : 0;
                var upperLeft = y > 0 && x >= bytesPerPixel ? result[prior + x - bytesPerPixel] : 0;
                result[row + x] = filter switch
                {
                    0 => raw,
                    1 => unchecked((byte)(raw + left)),
                    2 => unchecked((byte)(raw + above)),
                    3 => unchecked((byte)(raw + ((left + above) / 2))),
                    4 => unchecked((byte)(raw + Paeth(left, above, upperLeft))),
                    _ => raw
                };
            }
        }
        return result;
    }

    private static byte[] ToRgba(
        byte[] scan, int width, int height, int bitDepth, int colorType,
        byte[]? palette, byte[]? transparency)
    {
        var rgba = new byte[checked(width * height * 4)];
        var source = 0;
        var target = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                byte r, g, b, a = 255;
                switch (colorType)
                {
                    case 0:
                        r = g = b = scan[source++];
                        break;
                    case 2:
                        r = scan[source++]; g = scan[source++]; b = scan[source++];
                        break;
                    case 3:
                        var pixelsPerByte = 8 / bitDepth;
                        var packed = scan[source + (x / pixelsPerByte)];
                        var shift = 8 - bitDepth - ((x % pixelsPerByte) * bitDepth);
                        var index = (packed >> shift) & ((1 << bitDepth) - 1);
                        if (index * 3 + 2 >= palette!.Length)
                            throw new InvalidDataException("The PNG references a colour outside its palette.");
                        r = palette[index * 3]; g = palette[index * 3 + 1]; b = palette[index * 3 + 2];
                        a = transparency is not null && index < transparency.Length ? transparency[index] : (byte)255;
                        if (x == width - 1) source += (width * bitDepth + 7) / 8;
                        break;
                    case 4:
                        r = g = b = scan[source++]; a = scan[source++];
                        break;
                    case 6:
                        r = scan[source++]; g = scan[source++]; b = scan[source++]; a = scan[source++];
                        break;
                    default:
                        throw new InvalidDataException("Unsupported PNG colour type.");
                }
                rgba[target++] = r; rgba[target++] = g; rgba[target++] = b; rgba[target++] = a;
            }
        }
        return rgba;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static int ReadInt(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
}
