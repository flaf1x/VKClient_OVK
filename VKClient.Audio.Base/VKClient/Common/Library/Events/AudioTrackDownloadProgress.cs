namespace VKClient.Common.Library.Events
{
    public sealed class AudioTrackDownloadProgress
    {
        public string Id { get; set; }

        public float Progress { get; set; }
    }

    public sealed class AudioDownloadQueueProgress
    {
        public int Completed { get; set; }

        public int Failed { get; set; }

        public int Total { get; set; }

        public double Progress { get; set; }

        public bool IsActive { get; set; }
    }
}
