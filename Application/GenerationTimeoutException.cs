using System;

namespace Koca_Kafa.Application
{
    public sealed class GenerationTimeoutException : Exception
    {
        public GenerationTimeoutException(double elapsedSeconds, Exception innerException = null)
            : base("Generation timed out after " + elapsedSeconds.ToString("0.0") + " seconds.", innerException)
        {
            ElapsedSeconds = elapsedSeconds;
        }

        public double ElapsedSeconds { get; }
    }
}
