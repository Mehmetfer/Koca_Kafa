namespace Koca_Kafa.Application
{
    public sealed class StreamGenerationMetrics
    {
        public long? TimeToFirstTokenMs { get; set; }
        public long TotalGenerationMs { get; set; }
    }
}
