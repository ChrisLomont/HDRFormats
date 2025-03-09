using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

// Read and write the RGBE image format
// for RADIANCE, suffix often *.hdr or *.pic
// online viewer https://viewer.openhdr.org/
namespace Lomont.Formats
{
    public class HdrFormat
    {
        public enum Result
        {
            Ok,
            InvalidHeader,
            ReadError,
            FormatError
        }

        // default to Rec.2020 primaries
        public record HdrInfo(int Width, int Height, float? Gamma=null, float? Exposure=null, string PrimariesRgbw= "0.708 0.292 0.170 0.797 0.131 0.046 0.3127 0.3290", bool RunLengthEncoded = false);

        // write HDR image
        public static void WriteImage(string filename, HdrInfo info, List<float> data)
        {
            var (width, height) = (info.Width, info.Height);
            using var fs = File.Create(filename);
            var header =
                "#?RADIANCE\n" +
                (info.Gamma.HasValue ? $"GAMMA={info.Gamma.Value}\n" : "") +
                (info.Exposure.HasValue ? $"EXPOSURE={info.Exposure.Value}\n" : "") +
                (!String.IsNullOrEmpty(info.PrimariesRgbw) ? $"PRIMARIES={info.PrimariesRgbw}\n" : "") +
                "FORMAT=32-bit_rle_rgbe\n" +
                "\n" +
                $"-Y {height} +X {width}\n";
            var bh = Encoding.ASCII.GetBytes(header);
            fs.Write(bh);

            WritePixels(fs, data, width, height, info.RunLengthEncoded);
        }

        // read HDR image
        public static (Result result, HdrInfo info) ReadImage(string filename, List<float> output)
        {
            output.Clear();
            HdrInfo info = new(0, 0);
            // todo - make all this more robust
            var data = File.ReadAllBytes(filename);
            var dataPos = 0;
            string line;
            if (!ReadLine() || !(line == "#?RADIANCE" || line == "#?RGBE"))
            {
                return (Result.InvalidHeader,info);
            }

            var reg = new Regex("\\-Y (?<height>[0-9]+) \\+X (?<width>[0-9]+)");
            var done = false;
            var primariesTag = "PRIMARIES=";
            var gammaTag     = "GAMMA=";
            var exposureTag  = "EXPOSURE=";
            while (!done)
            {
                if (!ReadLine())
                {
                    return (Result.InvalidHeader, info);
                }
                if (line.StartsWith(primariesTag))
                {
                    var primaries = line.Substring(primariesTag.Length).Trim();
                    info = info with { PrimariesRgbw = primaries };
                } 
                else if (line.StartsWith("FORMAT=32-bit_rle_rgbe"))
                {
                    info = info with { RunLengthEncoded = true };
                }
                else if (line.StartsWith(gammaTag))
                {
                    var gammaText = line.Substring(gammaTag.Length).Trim();
                    info = info with { Gamma = float.Parse(gammaText)};
                }
                else if (line.StartsWith(exposureTag))
                {
                    var exposureText = line.Substring(exposureTag.Length).Trim();
                    info = info with { Exposure = float.Parse(exposureText) };
                }
                else if (reg.IsMatch(line))
                {
                    var match = reg.Match(line);
                    var width = int.Parse(match.Groups["width"].Value);
                    var height = int.Parse(match.Groups["height"].Value);
                    info = info with { Width = width, Height = height };
                    done = true; // last item
                }
                else
                {
                    return (Result.InvalidHeader, info);
                }
            }

            return (ReadPixelsRle(output, info.Width, info.Height, data, dataPos), info);

            bool ReadLine()
            {
                var start = dataPos;
                while (data[dataPos] != '\n')
                    dataPos++;

                var good = data[dataPos] == '\n';

                if (good)
                {
                    line = Encoding.ASCII.GetString(data.AsSpan(start, dataPos - start));
                    dataPos++; // skip '\n'
                    // skip empty strings
                    if (line == "") return ReadLine();
                    return true;
                }

                line = "<ERROR>";
                return false;
            }
        }

        #region Implementation

        static Result ReadPixelsRle(
            List<float> output,
            int width,
            int height,
            byte[] fileData,
            int filePos
        )
        {
            var rgbe = new byte[4];
            var buf = new byte[2];

            var scanlineBuffer = new byte[4 * width];

            // read bytes into d
            bool Read(byte[] d, int size, int start = 0)
            {
                for (var k = 0; k < size; ++k)
                    d[k + start] = fileData[filePos + k];
                filePos += size;
                return true;
            }

            if (width < 8 || width > 0x7FFF)
            {
                // todo - if width < 8 or > 7fff, must use raw pixels
                throw new NotImplementedException();
            }

            for (var h = 0; h < height; ++h)
            {

                if (!Read(rgbe, 4))
                {
                    return Result.ReadError;
                }

                if ((rgbe[0] != 2) || (rgbe[1] != 2) || (rgbe[2] & 0x80) != 0)
                {
                    // could decode pixel, and use non-RLE decode
                    return Result.FormatError; // wrong format
                }

                if ((rgbe[2] << 8 | rgbe[3]) != width)
                {
                    return Result.FormatError; // wrong scanline width
                }

                var ptr = 0;
                var ptrEnd = scanlineBuffer.Length;
                for (var i = 0; i < 4; i++)
                {
                    while (ptr < scanlineBuffer.Length)
                    {
                        if (!Read(buf, 2))
                        {
                            return Result.ReadError;
                        }

                        if (buf[0] > 128)
                        {
                            // run of same value
                            var count = buf[0] - 128;
                            if ((count == 0) || (count > ptrEnd - ptr))
                            {
                                return Result.FormatError; // bad scanline data
                            }

                            while (count-- > 0)
                            {
                                scanlineBuffer[ptr++] = buf[1];
                            }
                        }
                        else
                        {
                            // copy data
                            int count = buf[0];
                            if ((count == 0) || (count > ptrEnd - ptr))
                            {
                                return Result.FormatError; // bad scanline data
                            }

                            scanlineBuffer[ptr++] = buf[1];
                            if (--count > 0)
                            {
                                if (!Read(scanlineBuffer, count, ptr))
                                {
                                    return Result.ReadError;
                                }

                                ptr += count;
                            }
                        }
                    }
                }

                // buffer bytes into floats
                for (var i = 0; i < width; i++)
                {
                    rgbe[0] = scanlineBuffer[i];
                    rgbe[1] = scanlineBuffer[i + width];
                    rgbe[2] = scanlineBuffer[i + 2 * width];
                    rgbe[3] = scanlineBuffer[i + 3 * width];

                    var (r, g, b) = Rgbe2Float(rgbe);
                    output.Add(r);
                    output.Add(g);
                    output.Add(b);
                }
            }

            return Result.Ok;
        }

        /*
        Float32 format:
            1 sign bit S, 8 bit unsigned exponent E, 23 bits fractional part F
            non-denormal has implied 1 bit
            value f = (-1)^S * 2^(E-127) * (1.M)
        Our value are float R,G,B, each in [0,inf) as float32

        Let M = max(R,G,B). 
        If M == 0 (or M < some small threshold), then rgbe = 0,0,0,0, and decode this as R=G=B=0
        
        Else taking e = ceiling(Log2(M)) + 128 (forced that e in [1,255]):
        Let R' = R/(2^(e-128)), similar for G', B'
        Then the largest one, M, gives M' in [0.5,1), others are in [0,1).
        Let r = floor(R'*256) then in [0,255] (can show by float ops nothing can go out of bounds for bounds on e)
              = floor(R*2^(136-e))
        Similarly for g, b. This gives r,g,b,e

        To decode, since r= floor(value), to minimize avg expected error, add 0.5 to r before inverting, thus
        take R = (r+0.5)/256 * 2^(e-128) = (r+0.5) * 2^(e-136), similarly G,B.

        Can prove rounds trip rgbe -> RGB -> rgbe roundtrips for all values.
        */
        static (float r, float g, float b) Rgbe2Float(byte[] rgbe)
        {
            if (rgbe[3] != 0)
            {
                var f = MathF.Pow(2, rgbe[3] - 136);
                var r = (rgbe[0] + 0.5f) * f;
                var g = (rgbe[1] + 0.5f) * f;
                var b = (rgbe[2] + 0.5f) * f;
                return (r, g, b);
            }
            return (0, 0, 0);
        }

        static void Float2Rgbe(byte[] rgbe, float red, float green, float blue)
        {
            var m = Math.Max(Math.Max(red, green), blue);
            if (m < 1e-32) // todo - derive this carefully
            {
                rgbe[0] = rgbe[1] = rgbe[2] = rgbe[3] = 0;
            }
            else
            {
                var e = (int)(MathF.Ceiling(MathF.Log2(m)) + 128);
                var s = MathF.Pow(2, (136 - e));
                var r = (int)MathF.Floor(s * red);
                var g = (int)MathF.Floor(s * green);
                var b = (int)MathF.Floor(s * blue);

                // sanity checks
                Trace.Assert(0 <= r && r <= 255);
                Trace.Assert(0 <= g && g <= 255);
                Trace.Assert(0 <= b && b <= 255);
                Trace.Assert(0 <= e && e <= 255);

                rgbe[0] = (byte)r;
                rgbe[1] = (byte)g;
                rgbe[2] = (byte)b;
                rgbe[3] = (byte)e;
            }
        }


        static void WritePixels(
            FileStream fs,
            List<float> data,
            int width,
            int height,
            bool useRunLengthEncoding
            )
        {
            var rgbe = new byte[4];
            var buffer = new byte[width * 4];
            var dataPos = 0;

            // todo if width < 8 || width > 0x7FFF then write un-rle pixels

            for (var h = 0; h < height; ++h)
            {
                rgbe[0] = 2;
                rgbe[1] = 2;
                rgbe[2] = (byte)(width >> 8);
                rgbe[3] = (byte)(width & 0xFF);
                fs.Write(rgbe);
                int i;
                for (i = 0; i < width; i++)
                {
                    Float2Rgbe(rgbe, data[dataPos],
                        data[dataPos + 1], data[dataPos + 2]);

                    buffer[i] = rgbe[0];
                    buffer[i + width] = rgbe[1];
                    buffer[i + 2 * width] = rgbe[2];
                    buffer[i + 3 * width] = rgbe[3];
                    dataPos += 3;
                }

                // write each channel one at a time
                for (i = 0; i < 4; i++)
                {
                    if (useRunLengthEncoding)
                    {
                        WriteBytesRle(fs, buffer, i * width, (i + 1) * width);
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                }
            }
        }



        static void WriteBytesRle(
            FileStream fs,
            byte[] data,
            int curPosition,
            int stopPosition)
        {
            const int minRunLength = 4;
            var buf = new byte[2];

            while (curPosition < stopPosition)
            {
                var begRun = curPosition;
                // find next run if exists
                var oldRunCount = 0;
                var runCount = 0;
                while ((runCount < minRunLength) && (begRun < stopPosition))
                {
                    begRun += runCount;
                    oldRunCount = runCount;
                    runCount = 1;
                    while ((begRun + runCount < stopPosition) && (runCount < 127)
                                                              && (data[begRun] == data[begRun + runCount]))
                        runCount++;
                }

                // handle short run data
                if ((oldRunCount > 1) && (oldRunCount == begRun - curPosition))
                {
                    buf[0] = (byte)(128 + oldRunCount);
                    buf[1] = data[curPosition];
                    fs.Write(buf);
                    curPosition = begRun;
                }

                // copy bytes till start of next run
                while (curPosition < begRun)
                {
                    var copyLength = begRun - curPosition;
                    if (copyLength > 128)
                        copyLength = 128;
                    fs.WriteByte((byte)copyLength);
                    fs.Write(data, curPosition, copyLength);
                    curPosition += copyLength;
                }

                // next run if one was found
                if (runCount >= minRunLength)
                {
                    buf[0] = (byte)(128 + runCount);
                    buf[1] = data[begRun];
                    fs.Write(buf);
                    curPosition += runCount;
                }
            }
        }

        #endregion
    }
}
