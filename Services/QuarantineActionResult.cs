namespace CCSuitePro.Services
{
    public sealed class QuarantineActionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string QuarantinePath { get; set; } = string.Empty;
        public int FilesQuarantined { get; set; }
    }
}
