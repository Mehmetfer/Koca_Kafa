using System.Collections.Generic;
using Koca_Kafa.AI.Abstractions;
using Koca_Kafa.Data.Abstractions;
using Koca_Kafa.Memory.Abstractions;
using Koca_Kafa.Models;
using Koca_Kafa.Services.Abstractions;

namespace Koca_Kafa.Services
{
    public sealed class ConversationService : IConversationService
    {
        private readonly ISettingsRepository _settingsRepository;
        private readonly ISessionRepository _sessionRepository;
        private readonly IConversationMemory _memory;
        private readonly IPersonalityProvider _personality;

        public ConversationService(
            ISettingsRepository settingsRepository,
            ISessionRepository sessionRepository,
            IConversationMemory memory,
            IPersonalityProvider personality)
        {
            _settingsRepository = settingsRepository;
            _sessionRepository = sessionRepository;
            _memory = memory;
            _personality = personality;
        }

        public AppSettings Settings => _settingsRepository.Load();

        public IReadOnlyList<ChatMessage> CurrentMessages => _memory.Messages;

        public string SessionId => _memory.SessionId;

        public string AssistantName => _personality.AssistantName;

        public void StartNewSession()
        {
            var settings = Settings;
            _memory.Reset(null, _personality.CreateInitialMessages(settings.OwnerName));
        }

        public ChatMessage AddUserMessage(string text)
        {
            var message = new ChatMessage(ChatRole.User, text);
            _memory.Add(message);
            return message;
        }

        public void AddAssistantMessage(string text)
        {
            _memory.Add(new ChatMessage(ChatRole.Assistant, text));
        }

        public void PersistSession()
        {
            _sessionRepository.Save(new ConversationSession(_memory.SessionId, _memory.Messages));
        }

        public string GetUserDisplayName()
        {
            var owner = Settings.OwnerName;
            return string.IsNullOrWhiteSpace(owner) ? "Sen" : owner;
        }
    }
}
