using Koca_Kafa.Models;

namespace Koca_Kafa.Services.Cognitive
{
    public interface IResponseQualityEngine
    {
        string Polish(string reply, ResponseQualityContext context);
    }
}
