namespace Integround.Components.Files
{
    public class ReceiveFileResult
    {
        public bool IsError { get; set; }
        public string Message { get; set; }
        public string FileName { get; set; }
        public string TempFileName { get; set; }
        public string Path { get; set; }
    }
}
