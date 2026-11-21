namespace NovelShelf.Vision;

public sealed class TextModel(string folder, HttpClient http)
    : DownloadableModel<OnnxTextRegionDetector>(folder, http, FileName, Size, Sha256, Url)
{
    public const string FileName = "comic-text-and-bubble-detector-v4-s-int8.onnx";
    public const long Size = 11_120_765;
    public const string Sha256 = "5fe9e4f576e49d4e7e8b0e029d6d3cdc252abd4694113e1cae120e62c931ea79";
    public static readonly Uri Url =
        new("https://huggingface.co/ogkalu/comic-text-and-bubble-detector/resolve/main/detector-v4-s_int8.onnx");

    protected override OnnxTextRegionDetector Create(string path, string id) => new(path, id);
}
