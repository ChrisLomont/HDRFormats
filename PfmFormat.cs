using System.Text;

namespace Lomont.Format
{
    // free viewer https://imagetostl.com/view-pfm-online
    public class PfmFormat
    {
        public enum Result
        {
            Ok,
            InvalidHeader
        }

        public record PfmInfo(int Width, int Height);

        // write HDR image
        public static void WriteImage(string filename, PfmInfo info, List<float> output)
        {
            var (width, height) = (info.Width, info.Height);
            using var fs = File.Create(filename);

            var header =
                "PF\n" + // RGB
                $"{width} {height}\n" + // width, height
                "-1.0\n"; // little endian C#
            fs.Write(Encoding.ASCII.GetBytes(header));
            // left to right, bottom to top
            for (var h = height - 1; h >= 0; --h)
            {
                for (var w = 0; w < width; ++w)
                {
                    var index = (h * width + w) * 3;
                    fs.Write(BitConverter.GetBytes(output[index]));
                    fs.Write(BitConverter.GetBytes(output[index+1]));
                    fs.Write(BitConverter.GetBytes(output[index+2]));
                }
            }
        }

        // read HDR image
        public static (Result result, PfmInfo info) ReadImage(string filename, List<float> output)
        {
            output.Clear();
            // header 3 "lines", need not be \n, or \r, just some whitespace, so.....
            // PF = rgb, Pf = grayscale, then width, height,
            // then scale factor (negative is little endian, positive is big endian, need not be integer!
            // todo - how to be sure end of items? only one white space char?

            PfmInfo info = new(0, 0);
            // todo - make all this more robust
            var data = File.ReadAllBytes(filename);

            var dataPos = 0;
            string line;

            if (!GetToken() || !(line =="PF" || line == "Pf"))
            {
                return (Result.InvalidHeader, info);
            }
            if (data[1] == 'f')
            {
                throw new NotImplementedException("PFM grayscale unsupported");
            }
            if (!GetToken() || !Int32.TryParse(line, out var width))
            {
                return (Result.InvalidHeader, info);
            }
            if (!GetToken() || !Int32.TryParse(line, out var height))
            {
                return (Result.InvalidHeader, info);
            }

            if (!GetToken() || !Double.TryParse(line, out var scale) || scale >= 0)
            {
                return (Result.InvalidHeader, info);
            }

            // left to right, bottom to top, painful to read :)
            List<List<float>> rows = new();

            for (var h = height - 1; h >= 0; --h)
            {
                var row = new List<float>();
                for (var w = 0; w < width; ++w)
                {
                    row.Add(BitConverter.ToSingle(data, dataPos));
                    dataPos += 4;
                    row.Add(BitConverter.ToSingle(data, dataPos));
                    dataPos += 4;
                    row.Add(BitConverter.ToSingle(data, dataPos));
                    dataPos += 4;
                }
                rows.Add(row);
            }
            // reverse rows
            for (var h = height-1; h>=0; --h)
                output.AddRange(rows[h]);

            return (Result.Ok, new PfmInfo(width, height));

            // read next token, and 1 following whitespace character
            bool GetToken()
            {
                var token = "";
                var ws = " \t\r\n"; // skip these until not one of them
                while (dataPos < data.Length && ws.Contains((char)data[dataPos]))
                {
                    dataPos++;
                }
                // read till hits one
                while (dataPos < data.Length && !ws.Contains((char)data[dataPos]))
                {
                    token += (char)data[dataPos];
                    dataPos++;
                }
                // next must be whitespace, eat it
                var lastws = false;
                if (dataPos < data.Length && ws.Contains((char)data[dataPos]))
                {
                    dataPos++;
                    lastws = true;
                }

                line = token;
                return lastws && !String.IsNullOrEmpty(line);
            }
        }

    }
}
