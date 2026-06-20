using System.Collections.Generic;
using Koca_Kafa.Data.Abstractions;
using Koca_Kafa.Models;
using Koca_Kafa.Services.Abstractions;

namespace Koca_Kafa.Services
{
    public sealed class TrainingService : ITrainingService
    {
        private readonly ITrainingDataRepository _repository;

        public TrainingService(ITrainingDataRepository repository)
        {
            _repository = repository;
        }

        public void RecordExchange(ChatMessage userMessage, ChatMessage assistantMessage)
        {
            if (userMessage == null || assistantMessage == null)
                return;

            _repository.Append(new TrainingExample
            {
                Messages = new List<ChatMessage> { userMessage, assistantMessage }
            });
        }

        public int GetExampleCount() => _repository.GetExampleCount();

        public int Export(string outputPath) => _repository.ExportTo(outputPath);
    }
}
