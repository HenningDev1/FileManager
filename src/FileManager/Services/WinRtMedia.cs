using Avalonia.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FileManager.Services;

/// <summary>PDF rendering and video frame extraction through built-in Windows APIs (no extra codecs needed).</summary>
public static class WinRtMedia
{
    public sealed class Video
    {
        public required MediaComposition Composition { get; init; }
        public TimeSpan Duration { get; init; }
        public uint Width { get; init; }
        public uint Height { get; init; }
        public uint Bitrate { get; init; }
    }

    public sealed record ImageInfo(uint Width, uint Height, DateTimeOffset? Taken, string? Camera);
    public sealed record AudioInfo(string? Title, string? Artist, string? Album, TimeSpan Duration, uint Bitrate);

    public static async Task<PdfDocument> OpenPdf(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        return await PdfDocument.LoadFromFileAsync(file);
    }

    public static async Task<Bitmap> RenderPdfPage(PdfDocument doc, uint index, uint width)
    {
        using var page = doc.GetPage(index);
        using var ras = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(ras, new PdfPageRenderOptions { DestinationWidth = width });
        return ToBitmap(ras);
    }

    public static async Task<Video> OpenVideo(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var props = await file.Properties.GetVideoPropertiesAsync();
        var clip = await MediaClip.CreateFromFileAsync(file);
        var comp = new MediaComposition();
        comp.Clips.Add(clip);
        return new Video
        {
            Composition = comp,
            Duration = clip.OriginalDuration > TimeSpan.Zero ? clip.OriginalDuration : props.Duration,
            Width = props.Width, Height = props.Height, Bitrate = props.Bitrate,
        };
    }

    public static async Task<Bitmap?> Frame(Video v, TimeSpan at, int width, bool precise)
    {
        int height = v.Width > 0 && v.Height > 0 ? (int)(width * (double)v.Height / v.Width) : width * 9 / 16;
        if (at >= v.Duration) at = v.Duration - TimeSpan.FromMilliseconds(100);
        if (at < TimeSpan.Zero) at = TimeSpan.Zero;
        using var img = await v.Composition.GetThumbnailAsync(at, width, height,
            precise ? VideoFramePrecision.NearestFrame : VideoFramePrecision.NearestKeyFrame);
        return img is null ? null : ToBitmap(img);
    }

    public static async Task<ImageInfo?> GetImageInfo(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var p = await file.Properties.GetImagePropertiesAsync();
            DateTimeOffset? taken = p.DateTaken.Year > 1700 ? p.DateTaken : null;
            return new ImageInfo(p.Width, p.Height, taken, string.IsNullOrWhiteSpace(p.CameraModel) ? null : $"{p.CameraManufacturer} {p.CameraModel}".Trim());
        }
        catch { return null; }
    }

    public static async Task<AudioInfo?> GetAudioInfo(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var p = await file.Properties.GetMusicPropertiesAsync();
            return new AudioInfo(p.Title, p.Artist, p.Album, p.Duration, p.Bitrate);
        }
        catch { return null; }
    }

    static Bitmap ToBitmap(IRandomAccessStream ras)
    {
        ras.Seek(0);
        using var s = ras.AsStreamForRead();
        var ms = new MemoryStream();
        s.CopyTo(ms);
        ms.Position = 0;
        return new Bitmap(ms);
    }
}
