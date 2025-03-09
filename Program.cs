// Chris Lomont testing HDR image formats
using Lomont.Formats;
using Lomont.Format;

// command line must be path to where images live
// Visual Studio solution has this pointing to ../../../
var path = args[0]; 

foreach (var file in Directory.GetFiles(path))
{
    if (!file.EndsWith(".hdr") && !file.EndsWith(".pfm"))
        continue;

    var outBase = path + @"output\" + Path.GetFileNameWithoutExtension(file) + "_out";

    List<float> rgbIn = new();
    List<float> rgbRoundtripHdr = new();
    List<float> rgbRoundtripPfm = new();
    var readSuccess1 = true;
    var outHdrFn = outBase + ".hdr";
    var outPfmFn = outBase + ".pfm";

    int width = 0, height = 0;

    if (file.EndsWith(".hdr"))
    {
        var (resHdr, infoHdr) = HdrFormat.ReadImage(file, rgbIn);
        readSuccess1 = resHdr == HdrFormat.Result.Ok;
        HdrFormat.WriteImage(outHdrFn, infoHdr, rgbIn);
        PfmFormat.WriteImage(outPfmFn, new PfmFormat.PfmInfo(infoHdr.Width, infoHdr.Height), rgbIn);
        (width, height) = (infoHdr.Width, infoHdr.Height);
    }
    else if (file.EndsWith(".pfm"))
    {
        var (resPfm, infoPfm) = PfmFormat.ReadImage(file, rgbIn);
        readSuccess1 = resPfm == PfmFormat.Result.Ok;
        HdrFormat.WriteImage(outHdrFn, new HdrFormat.HdrInfo(infoPfm.Width, infoPfm.Height, RunLengthEncoded:true), rgbIn);
        PfmFormat.WriteImage(outPfmFn, infoPfm, rgbIn);
        (width, height) = (infoPfm.Width, infoPfm.Height);
    }


    var readSuccess2 = HdrFormat.ReadImage(outHdrFn, rgbRoundtripHdr).result == HdrFormat.Result.Ok;
    var readSuccess3 = PfmFormat.ReadImage(outPfmFn, rgbRoundtripPfm).result == PfmFormat.Result.Ok;

    // same buffers?
    var (same12, maxError12, avgError12) = CompareBuffers(rgbIn, rgbRoundtripHdr);
    var (same13, maxError13, avgError13) = CompareBuffers(rgbIn, rgbRoundtripPfm);

    // min, max
    var (minF, meanF,maxF) = (rgbIn.Min(), rgbIn.Sum()/rgbIn.Count,rgbIn.Max());


    var readSuccess = readSuccess1 && readSuccess2 && readSuccess3;
    Console.WriteLine(file);
    Console.Write($"    {width} x {height} : read successes {readSuccess}, same {same12} {same13},");
    Console.Write($" [min,mean,max] values [{minF:F3},{meanF:F3},{maxF:F3}],");
    Console.Write($" [max,avg] errors [{maxError12:F3},{avgError12:F3}] [{maxError13:F3},{avgError13:F3}]");
    Console.WriteLine();
}

(bool same, float maxError, float avgError) CompareBuffers(List<float> buf1, List<float> buf2)
{
    var m = Math.Max(buf1.Count, buf2.Count);
    if (m == 0) return (true, 0, 0);
    
    float max = 0.0f, sum = 0.0f;
    for (var i = 0; i < m; ++i)
    {
        var d = MathF.Abs(buf1[i] - buf2[i]);
        sum += d;
        max = Math.Max(max, d);
    }

    var avg = sum / m;

    var same = buf1.Count == buf2.Count && max < 1e-3;

    return (same,max,avg);
}