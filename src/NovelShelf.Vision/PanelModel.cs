namespace NovelShelf.Vision;

public sealed class PanelModel(string folder, HttpClient http)
    : DownloadableModel<OnnxPanelDetector>(folder, http, FileName, Size, Sha256, Url)
{
    public const string FileName = "comic-panel-bubble-yolo13n-640.onnx";
    public const long Size = 10_222_514;
    public const string Sha256 = "506242d8ce2239cf572f3951dafb433ddf630b3df5ab6389b7dd0d1de474a465";
    public static readonly Uri Url = new("https://huggingface.co/Gunnm92/comic-panel-bubble-detection-yolo13n/resolve/"
        + "891e15bc042279aab8718861e514805ec2dd23bd/onnx/yolo13n_panel-bubble_det_640_op18.onnx");

    protected override OnnxPanelDetector Create(string path, string id) => new(path, id);
}
