using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Koca_Kafa.AI.Abstractions;
using Koca_Kafa.Application.Abstractions;
using Koca_Kafa.Application.DTOs;
using Koca_Kafa.Data.Abstractions;
using Koca_Kafa.KnowledgeBase;
using Koca_Kafa.KnowledgeBase.Abstractions;
using Koca_Kafa.KnowledgeBase.Models;
using Koca_Kafa.Models;
using Koca_Kafa.MemoryStore;
using Koca_Kafa.Services.Abstractions;
using Koca_Kafa.Services.Background;
using Koca_Kafa.Services.Cognitive;
using Koca_Kafa.Services.Cognitive.Pipeline;
using Koca_Kafa.Core;
using Koca_Kafa.Core.Models;
using Koca_Kafa.Core.RuntimeContext;
using Koca_Kafa.Performance;
using Koca_Kafa.Services.Performance;
using Koca_Kafa.Training.Abstractions;

namespace Koca_Kafa.Application
{
    public sealed class ChatApplicationService : IChatApplicationService
    {
        private readonly IConversationService _conversationService;
        private readonly ILanguageModelClient _languageModelClient;
        private readonly ITrainingService _trainingService;
        private readonly ITrainingExportService _trainingExportService;
        private readonly ISettingsRepository _settingsRepository;
        private readonly IMemoryService _memoryService;
        private readonly IEmotionStateService _emotionStateService;
        private readonly IExperienceService _experienceService;
        private readonly IPersonalityEvolutionService _personalityEvolutionService;
        private readonly IReflectionService _reflectionService;
        private readonly IPersonalityProvider _personalityProvider;
        private readonly IRagService _ragService;
        private readonly IRagPriorityEngine _ragPriorityEngine;
        private readonly IAnswerExtractionService _answerExtractionService;
        private readonly IRetrievalDecisionRepository _retrievalDecisionRepository;
        private readonly IKnowledgeEvolutionService _knowledgeEvolutionService;
        private readonly IPerformanceProfiler _performanceProfiler;
        private readonly IBackgroundTaskCoordinator _backgroundTasks;
        private readonly IResponseGenerator _responseGenerator;
        private readonly ISelfCheckEngine _selfCheckEngine;
        private readonly ISelfDebugEngine _selfDebugEngine;
        private readonly IConversationBrain _conversationBrain;
        private readonly ICoreChatEngine _coreChatEngine;
        private readonly IUnifiedAssistantPipeline _unifiedPipeline;
        private readonly IDataPathProvider _paths;
        private readonly ChatGenerationCoordinator _generationCoordinator;

        public ChatApplicationService(
            IConversationService conversationService,
            ILanguageModelClient languageModelClient,
            ITrainingService trainingService,
            ITrainingExportService trainingExportService,
            ISettingsRepository settingsRepository,
            IMemoryService memoryService,
            IEmotionStateService emotionStateService,
            IExperienceService experienceService,
            IPersonalityEvolutionService personalityEvolutionService,
            IReflectionService reflectionService,
            IPersonalityProvider personalityProvider,
            IRagService ragService,
            IRagPriorityEngine ragPriorityEngine,
            IAnswerExtractionService answerExtractionService,
            IRetrievalDecisionRepository retrievalDecisionRepository,
            IKnowledgeEvolutionService knowledgeEvolutionService,
            IPerformanceProfiler performanceProfiler,
            IBackgroundTaskCoordinator backgroundTasks,
            IResponseGenerator responseGenerator,
            ISelfCheckEngine selfCheckEngine,
            ISelfDebugEngine selfDebugEngine,
            IConversationBrain conversationBrain,
            ICoreChatEngine coreChatEngine,
            IUnifiedAssistantPipeline unifiedPipeline,
            IDataPathProvider paths,
            ChatGenerationCoordinator generationCoordinator)
        {
            _conversationService = conversationService;
            _languageModelClient = languageModelClient;
            _trainingService = trainingService;
            _trainingExportService = trainingExportService;
            _settingsRepository = settingsRepository;
            _memoryService = memoryService;
            _emotionStateService = emotionStateService;
            _experienceService = experienceService;
            _personalityEvolutionService = personalityEvolutionService;
            _reflectionService = reflectionService;
            _personalityProvider = personalityProvider;
            _ragService = ragService;
            _ragPriorityEngine = ragPriorityEngine;
            _answerExtractionService = answerExtractionService;
            _retrievalDecisionRepository = retrievalDecisionRepository;
            _knowledgeEvolutionService = knowledgeEvolutionService;
            _performanceProfiler = performanceProfiler;
            _backgroundTasks = backgroundTasks;
            _responseGenerator = responseGenerator;
            _selfCheckEngine = selfCheckEngine;
            _selfDebugEngine = selfDebugEngine;
            _conversationBrain = conversationBrain;
            _coreChatEngine = coreChatEngine;
            _unifiedPipeline = unifiedPipeline;
            _paths = paths;
            _generationCoordinator = generationCoordinator;
        }

        public AppSettings GetSettings() => _settingsRepository.Load();

        public void UpdateSettings(AppSettings settings)
        {
            _settingsRepository.Save(settings);
            if (!string.IsNullOrWhiteSpace(settings?.OwnerName))
                _memoryService.AddMemory("Hitap", "Kullanıcıya şöyle hitap et: " + settings.OwnerName.Trim(), 90);
        }

        public bool NeedsOwnerSetup() => string.IsNullOrWhiteSpace(GetSettings().OwnerName);

        public ChatViewState StartNewConversation()
        {
            _generationCoordinator.CancelCurrent("new conversation");
            _conversationService.StartNewSession();
            _conversationBrain.ResetSession(_conversationService.SessionId);
            _unifiedPipeline.ResetSession(_conversationService.SessionId);
            return BuildViewState();
        }

        public void CancelActiveGeneration() =>
            _generationCoordinator.CancelCurrent("manual cancel");

        public async Task<OllamaStatus> GetOllamaStatusAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            var settings = GetSettings();
            var available = await _languageModelClient.IsAvailableAsync(cancellationToken).ConfigureAwait(false);

            return new OllamaStatus
            {
                IsAvailable = available,
                ModelName = settings.PreferredModel,
                StatusMessage = available
                    ? "Ollama: bağlı | Model: " + settings.PreferredModel
                    : "Ollama: kapalı — ollama.com adresinden kur, sonra: ollama pull qwen2.5:3b"
            };
        }

        public async Task<SendMessageResult> SendMessageAsync(
            string text,
            UiLatencyTrace latencyTrace = null,
            Action<string> onStreamToken = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new SendMessageResult
                {
                    Success = false,
                    ErrorMessage = "Boş mesaj gönderilemez."
                };
            }

            latencyTrace?.MarkSendMessageStart();

            var settings = GetSettings();
            var timeoutSeconds = Math.Max(
                5,
                settings.Chat?.GenerationTimeoutSeconds ?? ChatSettings.DefaultGenerationTimeoutSeconds);

            using (var generation = _generationCoordinator.Begin(text.Trim()))
            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                generation.Token,
                timeoutCts.Token,
                cancellationToken))
            {
                var activeToken = linkedCts.Token;
                var pipelineSw = Stopwatch.StartNew();
                var userMessage = _conversationService.AddUserMessage(text.Trim());
                var trace = _performanceProfiler.BeginMessage(text.Trim());
                var stageProfiler = new StageLatencyProfiler(_paths);

                try
                {
                    if (!ProductionPhaseRouter.ShouldUseFullPipeline(settings.Chat))
                    {
                        return await SendPhase1CoreMessageAsync(
                            settings,
                            userMessage,
                            text.Trim(),
                            trace,
                            stageProfiler,
                            pipelineSw,
                            latencyTrace,
                            onStreamToken,
                            activeToken).ConfigureAwait(false);
                    }

                    var userText = text.Trim();
                    var phaseCapabilities = ProductionPhaseRouter.ResolveCapabilities(settings.Chat);

                    var history = _conversationService.CurrentMessages.ToList();

                    if (phaseCapabilities.UseMemory)
                    {
                        latencyTrace?.MarkLearnStart();
                        var learnSw = Stopwatch.StartNew();
                        await _memoryService.LearnFromUserMessageAsync(userText, history, activeToken).ConfigureAwait(false);
                        learnSw.Stop();
                        latencyTrace?.MarkLearnEnd();
                        stageProfiler.Mark("Learn", learnSw.ElapsedMilliseconds);
                    }

                    trace.Metrics.EmotionUpdateDurationMs = trace.Measure(
                        () => _emotionStateService.ObserveUserMessage(userText, activeToken));

                    var cognitive = _responseGenerator.Prepare(
                        userText,
                        settings.OwnerName,
                        history,
                        stageProfiler);

                    var experience = _experienceService.GetCurrent();
                    var proactiveMemory = phaseCapabilities.UseMemory &&
                                          (settings.Chat?.ProactiveMemoryRecall ?? true) &&
                                          _memoryService.GetMemoryCount() > 0;

                    var pipelineResult = await _unifiedPipeline.ExecuteAsync(
                        new AssistantPipelineInput
                        {
                            SessionId = _conversationService.SessionId,
                            UserMessage = userText,
                            OwnerName = settings.OwnerName,
                            History = history,
                            Cognitive = cognitive,
                            DateTimeContext = DateTimeContext.CaptureNow(),
                            MemoryCount = phaseCapabilities.UseMemory ? _memoryService.GetMemoryCount() : 0,
                            Level = experience?.Level ?? 1,
                            AgeStage = experience?.AgeStage,
                            ProactiveMemory = proactiveMemory,
                            Phase = phaseCapabilities,
                            WebIntelligenceEnabled = settings.Chat?.WebIntelligenceEnabled ?? true,
                            CcgEnabled = settings.Chat?.CcgEnabled ?? true
                        },
                        activeToken).ConfigureAwait(false);

                    trace.Metrics.MemorySearched = !string.IsNullOrWhiteSpace(pipelineResult.FilteredMemoryContext);
                    trace.Metrics.MemoryHit = pipelineResult.HadHighConfidenceMemory;
                    stageProfiler.Mark("UnifiedPipeline", pipelineSw.ElapsedMilliseconds);

                    activeToken.ThrowIfCancellationRequested();

                    var preOllamaMs = pipelineSw.ElapsedMilliseconds;
                    latencyTrace?.MarkOllamaStart();
                    var ollamaSw = Stopwatch.StartNew();
                    var streamMetrics = new StreamGenerationMetrics();
                    string draftReply;

                    if (!pipelineResult.UseLlm && !string.IsNullOrWhiteSpace(pipelineResult.DirectReply))
                    {
                        draftReply = pipelineResult.DirectReply;
                        onStreamToken?.Invoke(draftReply);
                        ollamaSw.Stop();
                        latencyTrace?.MarkOllamaEnd();
                        trace.Metrics.OllamaRequestDurationMs = 0;
                        trace.Metrics.TimeToFirstTokenMs = 0;
                        stageProfiler.Mark("Ollama", 0);
                        stageProfiler.Mark("TTFT", 0);
                    }
                    else
                    {
                        draftReply = ReplySanitizer.Sanitize(
                            await GenerateDraftReplyAsync(
                                settings,
                                pipelineResult.LlmMessages,
                                userText,
                                onStreamToken,
                                streamMetrics,
                                latencyTrace,
                                activeToken).ConfigureAwait(false));
                        ollamaSw.Stop();
                        latencyTrace?.MarkOllamaEnd();
                        trace.Metrics.OllamaRequestDurationMs = ollamaSw.ElapsedMilliseconds;
                        trace.Metrics.TimeToFirstTokenMs = streamMetrics.TimeToFirstTokenMs ?? 0;
                        stageProfiler.Mark("Ollama", ollamaSw.ElapsedMilliseconds);
                        if (streamMetrics.TimeToFirstTokenMs.HasValue)
                            stageProfiler.Mark("TTFT", streamMetrics.TimeToFirstTokenMs.Value);
                    }

                    draftReply = ReplySanitizer.Sanitize(draftReply ?? string.Empty);

                    var totalToFirstToken = preOllamaMs + (streamMetrics.TimeToFirstTokenMs ?? 0);

                    activeToken.ThrowIfCancellationRequested();

                    var selfCheckContext = new SelfCheckContext
                    {
                        Intent = cognitive.LegacyIntent,
                        Plan = cognitive.Plan,
                        Empathy = cognitive.LegacyEmpathy,
                        MemoryContext = pipelineResult.FilteredMemoryContext,
                        HadMemoryResults = pipelineResult.HadHighConfidenceMemory,
                        UserMessage = userText,
                        MessageCategory = pipelineResult.BrainDecision?.MessageCategory ?? MessageCategory.CasualChat
                    };

                    var selfCheckSw = Stopwatch.StartNew();
                    var skipSelfCheckLlm = settings.Chat?.SkipSelfCheckLlmRevision ?? true;
                    var selfCheckOutcome = await _selfCheckEngine.ValidateAndReviseAsync(
                        userText,
                        draftReply,
                        selfCheckContext,
                        settings.PreferredModel,
                        activeToken,
                        skipSelfCheckLlm).ConfigureAwait(false);
                    selfCheckSw.Stop();
                    stageProfiler.Mark("SelfCheck", selfCheckSw.ElapsedMilliseconds);

                    var reply = ReplySanitizer.Sanitize(selfCheckOutcome.Reply);
                    if (string.IsNullOrWhiteSpace(reply))
                        reply = ReplySanitizer.Sanitize(draftReply);

                    activeToken.ThrowIfCancellationRequested();

                    var qualitySw = Stopwatch.StartNew();
                    reply = _responseGenerator.PolishResponse(
                        reply,
                        pipelineResult.OutputContext?.QualityContext ?? new ResponseQualityContext
                        {
                            OwnerName = settings.OwnerName,
                            UserMessage = userText,
                            MemoryContext = pipelineResult.FilteredMemoryContext,
                            HadMemoryResults = pipelineResult.HadHighConfidenceMemory
                        });

                    if (settings.Chat?.SelfDebugEnabled ?? true)
                    {
                        var selfDebugSw = Stopwatch.StartNew();
                        var debugOutcome = _selfDebugEngine.EvaluateAndRepair(new SelfDebugContext
                        {
                            UserMessage = userText,
                            Reply = reply,
                            DraftReply = draftReply,
                            MemoryContext = pipelineResult.FilteredMemoryContext,
                            WebContext = pipelineResult.WebContext,
                            OwnerName = settings.OwnerName,
                            OutputContext = pipelineResult.OutputContext ?? new ProductionOutputContext
                            {
                                QualityContext = new ResponseQualityContext
                                {
                                    UserMessage = userText,
                                    OwnerName = settings.OwnerName,
                                    MemoryContext = pipelineResult.FilteredMemoryContext
                                },
                                FilteredMemoryContext = pipelineResult.FilteredMemoryContext,
                                LanguageState = pipelineResult.LanguageState,
                                Intent = pipelineResult.Intent,
                                HadHighConfidenceMemory = pipelineResult.HadHighConfidenceMemory
                            },
                            SelfCheckOutcome = selfCheckOutcome,
                            KnowledgeKind = pipelineResult.OutputContext?.QualityContext?.KnowledgeKind
                                              ?? KnowledgeQuestionKind.None,
                            DecisionAction = pipelineResult.LockResult?.LockedDecision?.Action
                                             ?? pipelineResult.DecisionResult?.Action
                                             ?? DecisionBrainAction.ChatResponse,
                            DecisionActionSet = pipelineResult.LockResult?.LockedDecision != null ||
                                                pipelineResult.DecisionResult != null,
                            LanguageState = pipelineResult.LanguageState,
                            HadWebResults = pipelineResult.HadWebResults,
                            MaxIterations = Math.Max(1, Math.Min(settings.Chat?.MaxSelfDebugIterations ?? 2, 2))
                        });
                        reply = debugOutcome.Reply ?? reply;
                        selfDebugSw.Stop();
                        stageProfiler.Mark("SelfDebug", selfDebugSw.ElapsedMilliseconds);
                        pipelineResult.LayerTrace.SelfDebugExecuted = true;
                        pipelineResult.LayerTrace.LayersExecuted.Add("SelfDebug");

                        if (!debugOutcome.Passed || debugOutcome.Iterations.Count > 0)
                        {
                            var debugUserMessage = userText;
                            var debugCopy = debugOutcome;
                            _backgroundTasks.Queue("SelfDebugLog", () =>
                                SelfDebugLogger.Write(_paths.RootPath, debugUserMessage, debugCopy));
                        }
                    }

                    reply = ProductionOutputEnforcer.Enforce(
                        reply,
                        pipelineResult.OutputContext ?? new ProductionOutputContext
                        {
                            QualityContext = new ResponseQualityContext { UserMessage = userText, OwnerName = settings.OwnerName },
                            FilteredMemoryContext = pipelineResult.FilteredMemoryContext,
                            LanguageState = pipelineResult.LanguageState,
                            Intent = pipelineResult.Intent
                        });
                    qualitySw.Stop();
                    stageProfiler.Mark("Polish", qualitySw.ElapsedMilliseconds);
                    stageProfiler.Mark("ProductionOutput", qualitySw.ElapsedMilliseconds);

                    if (MissingResponseGuard.NeedsMinimumReply(userText, reply))
                    {
                        reply = MissingResponseGuard.BuildMinimumReply(
                            userText,
                            settings.OwnerName,
                            pipelineResult.FilteredMemoryContext);
                        reply = ProductionOutputEnforcer.Enforce(reply, pipelineResult.OutputContext);
                    }

                    if (pipelineResult.BrainDecision != null)
                    {
                        _conversationBrain.RecordAssistantResponse(
                            _conversationService.SessionId,
                            reply,
                            pipelineResult.BrainDecision);
                    }

                    WriteTtftBreakdown(
                        stageProfiler,
                        trace,
                        userText,
                        pipelineResult.Intent.ToString(),
                        pipelineResult.LlmMessages?[0]?.Content?.Length ?? 0,
                        totalToFirstToken,
                        selfCheckSw.ElapsedMilliseconds,
                        qualitySw.ElapsedMilliseconds);

                    stageProfiler.WriteBreakdown(
                        userText,
                        pipelineResult.Intent.ToString(),
                        reply);

                    _conversationService.AddAssistantMessage(reply);

                    var assistantMessage = new ChatMessage(ChatRole.Assistant, reply);
                    trace.Metrics.Success = true;

                    var replyCopy = reply;
                    _backgroundTasks.Queue("PostReply", async () =>
                    {
                        _conversationService.PersistSession();
                        await _emotionStateService
                            .ObserveAssistantMessageAsync(replyCopy, CancellationToken.None)
                            .ConfigureAwait(false);
                        _experienceService.ObserveExchange(userText, replyCopy, CancellationToken.None);
                        _personalityEvolutionService.ObserveExchange(userText, replyCopy, CancellationToken.None);
                        await _reflectionService
                            .RunIfNeededAsync(_conversationService.CurrentMessages, CancellationToken.None)
                            .ConfigureAwait(false);
                        _trainingService.RecordExchange(userMessage, assistantMessage);
                    });

                    return new SendMessageResult
                    {
                        Success = true,
                        UserMessage = userMessage,
                        AssistantMessage = assistantMessage,
                        TimeToFirstTokenMs = streamMetrics.TimeToFirstTokenMs,
                        GenerationDurationMs = streamMetrics.TotalGenerationMs
                    };
                }
                catch (OperationCanceledException) when (activeToken.IsCancellationRequested)
                {
                    trace.Metrics.Success = false;
                    var elapsedSeconds = pipelineSw.Elapsed.TotalSeconds;

                    if (timeoutCts.IsCancellationRequested && !generation.Token.IsCancellationRequested)
                    {
                        GenerationLog.Timeout(_paths, text.Trim(), elapsedSeconds);
                        return new SendMessageResult
                        {
                            Success = false,
                            WasTimedOut = true,
                            UserMessage = userMessage,
                            ErrorMessage = BuildTimeoutUserMessage(settings.OwnerName)
                        };
                    }

                    if (generation.Token.IsCancellationRequested)
                    {
                        GenerationLog.Cancelled(_paths, text.Trim(), "pipeline cancelled");
                    }

                    return new SendMessageResult
                    {
                        Success = false,
                        WasCancelled = true,
                        UserMessage = userMessage,
                        ErrorMessage = "Generation iptal edildi."
                    };
                }
                catch (GenerationTimeoutException)
                {
                    trace.Metrics.Success = false;
                    return new SendMessageResult
                    {
                        Success = false,
                        WasTimedOut = true,
                        UserMessage = userMessage,
                        ErrorMessage = BuildTimeoutUserMessage(settings.OwnerName)
                    };
                }
                catch (System.Exception ex)
                {
                    trace.Metrics.Success = false;
                    return new SendMessageResult
                    {
                        Success = false,
                        UserMessage = userMessage,
                        ErrorMessage = ex.Message
                    };
                }
                finally
                {
                    _performanceProfiler.Complete(trace);
                }
            }
        }

        private static string BuildTimeoutUserMessage(string ownerName)
        {
            var hitap = string.IsNullOrWhiteSpace(ownerName) ? "baba" : ownerName.Trim();
            return "Şu an düşünmekte biraz zorlanıyorum " + hitap + ".\n" +
                   "Soruyu biraz daha kısa sorabilir misin?";
        }

        public Task<IngestResult> IngestDocumentAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken))
        {
            Directory.CreateDirectory(_paths.KnowledgeBasePath);
            var destination = Path.Combine(_paths.KnowledgeBasePath, Path.GetFileName(filePath));
            File.Copy(filePath, destination, true);
            return _ragService.IngestFileAsync(destination, cancellationToken);
        }

        public Task<int> IngestDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default(CancellationToken)) =>
            _ragService.IngestDirectoryAsync(directoryPath, cancellationToken);

        public string GetKnowledgeBaseFolder() => _paths.KnowledgeBasePath;

        public int ExportTrainingData(string outputPath) => _trainingExportService.ExportDataset(outputPath);

        private async Task<SendMessageResult> SendPhase1CoreMessageAsync(
            AppSettings settings,
            ChatMessage userMessage,
            string userText,
            MessagePerformanceTrace trace,
            StageLatencyProfiler stageProfiler,
            Stopwatch pipelineSw,
            UiLatencyTrace latencyTrace,
            Action<string> onStreamToken,
            CancellationToken cancellationToken)
        {
            var history = _conversationService.CurrentMessages.ToList();
            var coreResult = _coreChatEngine.Process(userText, history);
            string reply;

            if (!coreResult.UseLlm)
            {
                reply = coreResult.Reply ?? CoreChatOutputContract.FallbackClarify();
                onStreamToken?.Invoke(reply);
                trace.Metrics.OllamaRequestDurationMs = 0;
                trace.Metrics.TimeToFirstTokenMs = 0;
                stageProfiler.Mark("Ollama", 0);
                stageProfiler.Mark("TTFT", 0);
            }
            else
            {
                var messages = BuildPhase1Messages(coreResult, history, settings);
                var streamMetrics = new StreamGenerationMetrics();
                latencyTrace?.MarkOllamaStart();
                var ollamaSw = Stopwatch.StartNew();

                reply = ReplySanitizer.Sanitize(
                    await GenerateDraftReplyAsync(
                        settings,
                        messages,
                        userText,
                        onStreamToken,
                        streamMetrics,
                        latencyTrace,
                        cancellationToken).ConfigureAwait(false));

                ollamaSw.Stop();
                latencyTrace?.MarkOllamaEnd();
                trace.Metrics.OllamaRequestDurationMs = ollamaSw.ElapsedMilliseconds;
                trace.Metrics.TimeToFirstTokenMs = streamMetrics.TimeToFirstTokenMs ?? 0;
                stageProfiler.Mark("Ollama", ollamaSw.ElapsedMilliseconds);
                if (streamMetrics.TimeToFirstTokenMs.HasValue)
                    stageProfiler.Mark("TTFT", streamMetrics.TimeToFirstTokenMs.Value);

                reply = CoreChatOutputContract.Enforce(reply, coreResult.Intent);
            }

            reply = ReplySanitizer.Sanitize(reply ?? CoreChatOutputContract.ResolveUnknown(userText));
            _conversationService.AddAssistantMessage(reply);

            var assistantMessage = new ChatMessage(ChatRole.Assistant, reply);
            trace.Metrics.Success = true;
            stageProfiler.Mark("Phase1Core", pipelineSw.ElapsedMilliseconds);

            _backgroundTasks.Queue("PostReplyPhase1", () =>
            {
                _conversationService.PersistSession();
            });

            return new SendMessageResult
            {
                Success = true,
                UserMessage = userMessage,
                AssistantMessage = assistantMessage,
                TimeToFirstTokenMs = trace.Metrics.TimeToFirstTokenMs,
                GenerationDurationMs = trace.Metrics.OllamaRequestDurationMs
            };
        }

        private static IList<ChatMessage> BuildPhase1Messages(
            CoreChatResult coreResult,
            IList<ChatMessage> history,
            AppSettings settings)
        {
            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, coreResult.SystemPrompt ?? string.Empty)
            };

            var maxHistory = Math.Max(4, settings.Chat?.MaxHistoryMessages ?? ChatSettings.DefaultMaxHistoryMessages);
            var trimmed = history
                .Where(m => m.Role != ChatRole.System)
                .ToList();

            if (trimmed.Count > maxHistory)
                trimmed = trimmed.Skip(trimmed.Count - maxHistory).ToList();

            foreach (var message in trimmed)
                messages.Add(message);

            return messages;
        }

        private async Task<string> GenerateDraftReplyAsync(
            AppSettings settings,
            IList<ChatMessage> messages,
            string userInput,
            Action<string> onStreamToken,
            StreamGenerationMetrics metrics,
            UiLatencyTrace latencyTrace,
            CancellationToken cancellationToken)
        {
            var model = settings.PreferredModel;
            if (settings.Chat?.FastResponseMode == true &&
                !string.IsNullOrWhiteSpace(settings.Chat.FastPreferredModel))
            {
                model = settings.Chat.FastPreferredModel.Trim();
            }

            var stopwatch = Stopwatch.StartNew();
            long? ttftMs = null;
            var loggedTtft = false;

            Action<string> relayToken = token =>
            {
                if (string.IsNullOrEmpty(token))
                    return;

                if (!ttftMs.HasValue)
                {
                    ttftMs = stopwatch.ElapsedMilliseconds;
                    metrics.TimeToFirstTokenMs = ttftMs;
                    latencyTrace?.MarkFirstToken();

                    if (!loggedTtft)
                    {
                        loggedTtft = true;
                        GenerationLog.Ttft(_paths, userInput, ttftMs.Value);
                    }
                }

                onStreamToken?.Invoke(token);
            };

            var reply = await _languageModelClient
                .StreamGenerateReplyAsync(model, messages, relayToken, cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();
            metrics.TotalGenerationMs = stopwatch.ElapsedMilliseconds;

            var resolvedTtft = ttftMs ?? stopwatch.ElapsedMilliseconds;
            StreamingMetricsStore.Record(_paths, resolvedTtft);
            var averageTtft = StreamingMetricsStore.GetAverageTtftMs(_paths);
            GenerationLog.Completed(
                _paths,
                userInput,
                resolvedTtft,
                stopwatch.ElapsedMilliseconds,
                reply?.Length ?? 0,
                averageTtft);

            return reply;
        }

        private async Task<PromptBuildResult> BuildMessagesForModelAsync(
            string userQuery,
            MessagePerformanceTrace trace,
            StageLatencyProfiler stageProfiler,
            bool fastMode,
            CancellationToken cancellationToken)
        {
            var buildTotal = Stopwatch.StartNew();
            var settings = GetSettings();
            var cognitive = _responseGenerator.Prepare(
                userQuery,
                settings.OwnerName,
                _conversationService.CurrentMessages.ToList(),
                stageProfiler);

            var intentAnalysis = cognitive.LegacyIntent;
            var intentContext = fastMode ? TrimBlock(cognitive.IntentPrompt, 400) : cognitive.IntentPrompt;
            var empathyAnalysis = cognitive.LegacyEmpathy;
            var plan = cognitive.Plan;
            var knowledgeKind = plan?.KnowledgeKind ?? KnowledgeQuestionClassifier.Classify(userQuery);
            var isKnowledgeQuery = knowledgeKind != KnowledgeQuestionKind.None;
            var creativeTaskKind = plan?.CreativeTaskKind ?? CreativeTaskEngine.Classify(userQuery);
            var isCreativeTask = creativeTaskKind != CreativeTaskKind.None;
            var messageCategory = MessageCategoryClassifier.Classify(userQuery);
            var isEmotionalStatement = messageCategory == MessageCategory.EmotionalStatement;
            var isImplicitEmotional = messageCategory == MessageCategory.ImplicitEmotionalStatement;
            var implicitEmotion = ImplicitEmotionDetector.Detect(userQuery);
            var planContext = fastMode ? TrimBlock(cognitive.PlanPrompt, 300) : cognitive.PlanPrompt;
            var empathyContext = fastMode ? TrimBlock(cognitive.EmpathyPrompt, 300) : cognitive.EmpathyPrompt;
            var runtimeDateTime = DateTimeContext.CaptureNow();
            var isDateTimeQuery = DateTimeAwarenessEngine.IsDateTimeQuestion(userQuery);
            var runtimeContextBlock = DateTimeAwarenessEngine.BuildPromptBlock(runtimeDateTime);
            var dateTimeDirective = isDateTimeQuery
                ? DateTimeAwarenessEngine.BuildAnswerDirective(runtimeDateTime)
                : string.Empty;
            if (isDateTimeQuery)
                empathyContext = string.Empty;
            if (isKnowledgeQuery)
                empathyContext = string.Empty;
            if (isCreativeTask)
                empathyContext = string.Empty;
            var personaContext = fastMode ? TrimBlock(cognitive.PersonaPrompt, 500) : cognitive.PersonaPrompt;
            var personalityBlock = fastMode
                ? _personalityProvider.BuildCompactSystemPrompt(settings.OwnerName)
                : _personalityProvider.BuildSystemPrompt(settings.OwnerName);
            var traitsContext = fastMode ? string.Empty : _personalityEvolutionService.BuildPromptContext();

            long knowledgeMs;
            string knowledgeContext;
            if (fastMode && cognitive.Intent?.IntentType != IntentType.FactualQuery && !isKnowledgeQuery && !isCreativeTask)
            {
                knowledgeMs = 0;
                knowledgeContext = string.Empty;
            }
            else
            {
                knowledgeContext = trace.Measure(
                    () => _knowledgeEvolutionService.BuildPromptContext(),
                    out knowledgeMs);
            }
            trace.Metrics.KnowledgeProfileDurationMs = knowledgeMs;

            var emotionContext = fastMode ? string.Empty : _emotionStateService.BuildPromptContext();
            var experienceContext = fastMode ? string.Empty : _experienceService.BuildPromptContext();

            var memoryCount = _memoryService.GetMemoryCount();
            var proactiveMemory = (settings.Chat?.ProactiveMemoryRecall ?? true)
                && memoryCount > 0
                && userQuery.Length >= 4;

            var shouldSearchMemory = plan.RequiredMemory || proactiveMemory;
            var shouldSearchRag = plan.RequiredRag &&
                (!fastMode || cognitive.Intent?.IntentType == IntentType.FactualQuery || isKnowledgeQuery) &&
                !isCreativeTask;

            RagPriorityEvaluation ragEvaluation = null;
            string memoryContext = string.Empty;
            string lessonsContext = fastMode ? string.Empty : _reflectionService.BuildPromptContext(6);
            string ragContext = string.Empty;
            string strictRagBlock = string.Empty;
            string directAnswerBlock = string.Empty;

            if (shouldSearchMemory || shouldSearchRag)
            {
                long ragMs = 0;
                long memoryMs = 0;

                Task<RagPriorityEvaluation> ragTask = null;
                Task<string> memoryTask = null;

                if (shouldSearchRag)
                    ragTask = RunRagSearchAsync(userQuery, sw => ragMs = sw, cancellationToken);
                if (shouldSearchMemory)
                    memoryTask = RunMemorySearchAsync(userQuery, fastMode, plan.RequiredMemory, sw => memoryMs = sw, cancellationToken);

                var pending = new List<Task>();
                if (ragTask != null)
                    pending.Add(ragTask);
                if (memoryTask != null)
                    pending.Add(memoryTask);

                if (pending.Count > 0)
                    await Task.WhenAll(pending).ConfigureAwait(false);

                if (ragTask != null)
                {
                    ragEvaluation = await ragTask.ConfigureAwait(false);
                    trace.Metrics.RagSearchDurationMs = ragMs;
                    trace.Metrics.RagSearched = true;
                    trace.Metrics.RagHit = ragEvaluation?.Results != null && ragEvaluation.Results.Count > 0;
                    stageProfiler.Mark("RAG", ragMs);

                    try
                    {
                        _retrievalDecisionRepository.Insert(ragEvaluation.Decision);
                    }
                    catch
                    {
                        // audit failure should not block chat
                    }

                    ragContext = _ragService.BuildContextFromResults(ragEvaluation.Results);

                    if (UsesRagFirstOrder(ragEvaluation.Decision.Mode))
                    {
                        strictRagBlock = BuildModeBlock(ragEvaluation.Decision.Mode);

                        if (ragEvaluation.Decision.Mode == RagRetrievalMode.DirectAnswer)
                        {
                            var extracted = await _answerExtractionService
                                .TryExtractAsync(userQuery, ragEvaluation.Results, cancellationToken)
                                .ConfigureAwait(false);
                            if (extracted.Success)
                            {
                                directAnswerBlock = RagPromptBlocks.DirectAnswerBlock +
                                    "\n\nÇıkarılan cevap: " + extracted.Answer;
                                if (!string.IsNullOrWhiteSpace(extracted.SourceFileName))
                                    directAnswerBlock += "\nKaynak: " + extracted.SourceFileName;
                            }
                        }
                    }
                }

                if (memoryTask != null)
                {
                    memoryContext = await memoryTask.ConfigureAwait(false);
                    trace.Metrics.MemorySearchDurationMs = memoryMs;
                    trace.Metrics.MemorySearched = true;
                    trace.Metrics.MemoryHit = !string.IsNullOrWhiteSpace(memoryContext);
                    stageProfiler.Mark("Memory", memoryMs);
                }
            }

            var memoryRecallMode = plan.RequiredMemory && !string.IsNullOrWhiteSpace(memoryContext);
            var knowledgeDirective = isKnowledgeQuery
                ? KnowledgeResponseEngine.BuildPromptDirective(knowledgeKind)
                : string.Empty;
            var creativeTaskDirective = isCreativeTask
                ? CreativeTaskEngine.BuildPromptDirective(
                    creativeTaskKind,
                    userQuery,
                    _conversationService.CurrentMessages.ToList())
                : string.Empty;
            var empathyDirective = isEmotionalStatement
                ? EmpathyResponseEngine.BuildPromptDirective()
                : isImplicitEmotional && implicitEmotion.Confidence > ImplicitEmotionDetector.EmpathyThreshold
                    ? EmpathyResponseEngine.BuildImplicitPromptDirective(implicitEmotion)
                    : string.Empty;
            var systemPrompt = ComposeSystemPrompt(
                ragEvaluation?.Decision?.Mode ?? RagRetrievalMode.Normal,
                strictRagBlock,
                directAnswerBlock,
                intentContext,
                empathyContext,
                planContext,
                personaContext,
                ragContext,
                memoryContext,
                lessonsContext,
                personalityBlock,
                traitsContext,
                knowledgeContext,
                emotionContext,
                experienceContext,
                memoryRecallMode,
                knowledgeDirective,
                runtimeContextBlock,
                dateTimeDirective,
                creativeTaskDirective,
                empathyDirective);

            TryWritePromptDebug(
                ragEvaluation?.Decision,
                strictRagBlock,
                directAnswerBlock,
                ragContext,
                memoryContext,
                lessonsContext,
                personalityBlock,
                traitsContext,
                knowledgeContext,
                emotionContext,
                experienceContext,
                systemPrompt);

            var messages = new List<ChatMessage>
            {
                new ChatMessage(ChatRole.System, systemPrompt)
            };

            var maxHistory = fastMode
                ? Math.Max(4, settings.Chat?.MaxHistoryMessages ?? ChatSettings.DefaultMaxHistoryMessages)
                : int.MaxValue;
            var history = _conversationService.CurrentMessages
                .Where(m => m.Role != ChatRole.System)
                .ToList();
            if (history.Count > maxHistory)
                history = history.Skip(history.Count - maxHistory).ToList();

            foreach (var message in history)
                messages.Add(message);

            buildTotal.Stop();
            var promptComposeMs = Math.Max(
                0,
                buildTotal.ElapsedMilliseconds
                - trace.Metrics.MemorySearchDurationMs
                - trace.Metrics.RagSearchDurationMs
                - trace.Metrics.KnowledgeProfileDurationMs);
            stageProfiler.Mark("PromptCompose", promptComposeMs);
            trace.Metrics.PromptBuildDurationMs = promptComposeMs;

            return new PromptBuildResult
            {
                Messages = messages,
                RagEvaluation = ragEvaluation,
                RagContext = ragContext,
                SystemPromptChars = systemPrompt?.Length ?? 0,
                SelfCheckContext = new SelfCheckContext
                {
                    Intent = intentAnalysis,
                    Plan = plan,
                    Empathy = empathyAnalysis,
                    RagContext = ragContext,
                    RagMode = ragEvaluation?.Decision?.Mode,
                    HadRagResults = !string.IsNullOrWhiteSpace(ragContext),
                    HadMemoryResults = !string.IsNullOrWhiteSpace(memoryContext),
                    MemoryContext = memoryContext,
                    KnowledgeKind = knowledgeKind,
                    UserMessage = userQuery,
                    IsDateTimeQuestion = isDateTimeQuery,
                    CreativeTaskKind = creativeTaskKind,
                    MessageCategory = messageCategory,
                    ImplicitEmotionConfidence = implicitEmotion.Confidence
                },
                MemoryContext = memoryContext,
                DateTimeContext = runtimeDateTime
            };
        }

        private async Task<RagPriorityEvaluation> RunRagSearchAsync(
            string userQuery,
            Action<long> recordDuration,
            CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                return await _ragPriorityEngine
                    .EvaluateAsync(userQuery, DocumentEmbeddingService.DefaultTopK, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                sw.Stop();
                recordDuration(sw.ElapsedMilliseconds);
            }
        }

        private async Task<string> RunMemorySearchAsync(
            string userQuery,
            bool fastMode,
            bool requiredMemory,
            Action<long> recordDuration,
            CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var limit = MemoryConflictResolver.MaxTopicMemories;
                return await _memoryService.BuildContextForQueryAsync(
                    userQuery,
                    limit,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                sw.Stop();
                recordDuration(sw.ElapsedMilliseconds);
            }
        }

        private static bool UsesRagFirstOrder(RagRetrievalMode mode) =>
            mode == RagRetrievalMode.RagPreferred ||
            mode == RagRetrievalMode.StrictRag ||
            mode == RagRetrievalMode.DirectAnswer;

        private static string BuildModeBlock(RagRetrievalMode mode)
        {
            switch (mode)
            {
                case RagRetrievalMode.DirectAnswer:
                case RagRetrievalMode.StrictRag:
                    return RagPromptBlocks.StrictRagBlock;
                case RagRetrievalMode.RagPreferred:
                    return RagPromptBlocks.RagPreferredBlock;
                default:
                    return string.Empty;
            }
        }

        private static string ComposeSystemPrompt(
            RagRetrievalMode mode,
            string strictRagBlock,
            string directAnswerBlock,
            string intentContext,
            string empathyContext,
            string planContext,
            string personaContext,
            string ragContext,
            string memoryContext,
            string lessonsContext,
            string personalityBlock,
            string traitsContext,
            string knowledgeContext,
            string emotionContext,
            string experienceContext,
            bool memoryRecallMode,
            string knowledgeDirective,
            string runtimeContextBlock,
            string dateTimeDirective,
            string creativeTaskDirective,
            string empathyDirective)
        {
            var parts = new List<string>();
            AppendIfNotEmpty(parts, runtimeContextBlock);
            var recallDirective = memoryRecallMode
                ? "ÖNCELİK: Kullanıcı hafızadaki bilgileri soruyor. Aşağıdaki kalıcı hafızayı kullan; 'bilmiyorum' deme."
                : string.Empty;
            var hasEmpathyDirective = !string.IsNullOrWhiteSpace(empathyDirective);

            if (UsesRagFirstOrder(mode))
            {
                AppendIfNotEmpty(parts, strictRagBlock);
                AppendIfNotEmpty(parts, directAnswerBlock);
                AppendIfNotEmpty(parts, empathyDirective);
                AppendIfNotEmpty(parts, dateTimeDirective);
                AppendIfNotEmpty(parts, creativeTaskDirective);
                AppendIfNotEmpty(parts, knowledgeDirective);
                AppendIfNotEmpty(parts, recallDirective);
                AppendIfNotEmpty(parts, memoryContext);
                AppendIfNotEmpty(parts, ragContext);
                AppendIfNotEmpty(parts, intentContext);
                AppendIfNotEmpty(parts, planContext);
                AppendIfNotEmpty(parts, personaContext);
                AppendIfNotEmpty(parts, lessonsContext);
                AppendIfNotEmpty(parts, personalityBlock);
                AppendIfNotEmpty(parts, traitsContext);
                AppendIfNotEmpty(parts, knowledgeContext);
                AppendIfNotEmpty(parts, emotionContext);
                AppendIfNotEmpty(parts, experienceContext);
                if (!memoryRecallMode || hasEmpathyDirective)
                    AppendIfNotEmpty(parts, empathyContext);
            }
            else if (memoryRecallMode || !string.IsNullOrWhiteSpace(knowledgeDirective) ||
                     !string.IsNullOrWhiteSpace(dateTimeDirective) ||
                     !string.IsNullOrWhiteSpace(creativeTaskDirective) ||
                     hasEmpathyDirective)
            {
                AppendIfNotEmpty(parts, empathyDirective);
                AppendIfNotEmpty(parts, dateTimeDirective);
                AppendIfNotEmpty(parts, creativeTaskDirective);
                AppendIfNotEmpty(parts, knowledgeDirective);
                AppendIfNotEmpty(parts, recallDirective);
                AppendIfNotEmpty(parts, memoryContext);
                AppendIfNotEmpty(parts, ragContext);
                AppendIfNotEmpty(parts, personalityBlock);
                AppendIfNotEmpty(parts, personaContext);
                AppendIfNotEmpty(parts, intentContext);
                AppendIfNotEmpty(parts, planContext);
                AppendIfNotEmpty(parts, traitsContext);
                AppendIfNotEmpty(parts, knowledgeContext);
                AppendIfNotEmpty(parts, emotionContext);
                AppendIfNotEmpty(parts, experienceContext);
                AppendIfNotEmpty(parts, lessonsContext);
                if (!memoryRecallMode &&
                    string.IsNullOrWhiteSpace(knowledgeDirective) &&
                    string.IsNullOrWhiteSpace(dateTimeDirective) &&
                    string.IsNullOrWhiteSpace(creativeTaskDirective) &&
                    !hasEmpathyDirective)
                    AppendIfNotEmpty(parts, empathyContext);
            }
            else
            {
                AppendIfNotEmpty(parts, empathyDirective);
                AppendIfNotEmpty(parts, dateTimeDirective);
                AppendIfNotEmpty(parts, creativeTaskDirective);
                AppendIfNotEmpty(parts, personalityBlock);
                AppendIfNotEmpty(parts, personaContext);
                AppendIfNotEmpty(parts, intentContext);
                if (string.IsNullOrWhiteSpace(dateTimeDirective) &&
                    string.IsNullOrWhiteSpace(creativeTaskDirective) &&
                    !hasEmpathyDirective)
                    AppendIfNotEmpty(parts, empathyContext);
                AppendIfNotEmpty(parts, planContext);
                AppendIfNotEmpty(parts, traitsContext);
                AppendIfNotEmpty(parts, knowledgeContext);
                AppendIfNotEmpty(parts, emotionContext);
                AppendIfNotEmpty(parts, experienceContext);
                AppendIfNotEmpty(parts, memoryContext);
                AppendIfNotEmpty(parts, lessonsContext);
                AppendIfNotEmpty(parts, ragContext);
            }

            return string.Join("\n\n", parts);
        }

        private static void AppendIfNotEmpty(ICollection<string> parts, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add(value.Trim());
        }

        private static string TrimBlock(string value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxChars)
                return value ?? string.Empty;
            return value.Substring(0, maxChars) + "...";
        }

        private void WriteTtftBreakdown(
            StageLatencyProfiler stageProfiler,
            MessagePerformanceTrace trace,
            string input,
            string intentLabel,
            int systemPromptChars,
            long totalToFirstTokenMs,
            long selfCheckMs,
            long polishMs)
        {
            try
            {
                var intentMs = SumStages(stageProfiler, "Intent", "Empathy", "Planning", "Persona");
                var memoryMs = trace.Metrics.MemorySearchDurationMs;
                var ragMs = trace.Metrics.RagSearchDurationMs;
                var ollamaTtft = trace.Metrics.TimeToFirstTokenMs;
                var promptCompose = GetStage(stageProfiler, "PromptCompose");

                TtftBreakdownWriter.Record(_paths, new TtftBreakdownSnapshot
                {
                    Input = input,
                    IntentLabel = intentLabel,
                    IntentMs = intentMs,
                    MemoryMs = memoryMs,
                    RagMs = ragMs,
                    OllamaFirstTokenMs = ollamaTtft,
                    SelfCheckMs = selfCheckMs > 0 ? selfCheckMs : GetStage(stageProfiler, "SelfCheck"),
                    PolishMs = polishMs > 0 ? polishMs : GetStage(stageProfiler, "Polish"),
                    LearnMs = GetStage(stageProfiler, "Learn"),
                    PromptComposeMs = promptCompose,
                    TotalToFirstTokenMs = totalToFirstTokenMs,
                    SystemPromptChars = systemPromptChars
                });
            }
            catch
            {
                // logging must not break chat
            }
        }

        private static long GetStage(StageLatencyProfiler profiler, string name)
        {
            return profiler?.TryGetStage(name) ?? 0;
        }

        private static long SumStages(StageLatencyProfiler profiler, params string[] names)
        {
            if (profiler == null || names == null)
                return 0;
            long total = 0;
            foreach (var name in names)
                total += profiler.TryGetStage(name);
            return total;
        }

        private void TryWriteRagDebug(string userQuery, PromptBuildResult buildResult)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userQuery))
                    return;

                if (!string.Equals(
                        userQuery.Trim(),
                        "Koca Kafa hangi platformda geliştirildi?",
                        System.StringComparison.OrdinalIgnoreCase))
                    return;

                var decision = buildResult?.RagEvaluation?.Decision;
                var ragContext = buildResult?.RagContext ?? string.Empty;

                var logsDir = Path.Combine(_paths.RootPath, "Logs");
                Directory.CreateDirectory(logsDir);
                var path = Path.Combine(logsDir, "rag_debug.txt");

                var sb = new StringBuilder();
                sb.AppendLine("=== Koca Kafa RAG Debug ===");
                sb.AppendLine("TimestampUtc: " + System.DateTime.UtcNow.ToString("o"));
                sb.AppendLine("Query: " + userQuery);
                if (decision != null)
                {
                    sb.AppendLine("Top1Score: " + decision.Top1Score.ToString("0.000"));
                    sb.AppendLine("Top2Score: " + decision.Top2Score.ToString("0.000"));
                    sb.AppendLine("Confidence: " + decision.Confidence.ToString("0.000"));
                    sb.AppendLine("Mode: " + decision.Mode);
                }
                sb.AppendLine("ContextLength: " + ragContext.Length);
                sb.AppendLine();
                sb.AppendLine("## Context (first 500 chars)");
                sb.AppendLine(ragContext.Length <= 500 ? ragContext : ragContext.Substring(0, 500));
                sb.AppendLine();
                sb.AppendLine("## Context (full)");
                sb.AppendLine(ragContext);

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // ignore logging failures
            }
        }

        private void TryWritePromptDebug(
            RetrievalDecision decision,
            string strictRagBlock,
            string directAnswerBlock,
            string ragContext,
            string memoryContext,
            string lessonsContext,
            string personalityBlock,
            string traitsContext,
            string knowledgeContext,
            string emotionContext,
            string experienceContext,
            string fullSystemPrompt)
        {
            try
            {
                var logsDir = Path.Combine(_paths.RootPath, "Logs");
                Directory.CreateDirectory(logsDir);
                var path = Path.Combine(logsDir, "prompt_debug.txt");

                var sb = new StringBuilder();
                sb.AppendLine("=== Koca Kafa Prompt Debug ===");
                sb.AppendLine("TimestampUtc: " + System.DateTime.UtcNow.ToString("o"));
                sb.AppendLine("TotalCharacters: " + (fullSystemPrompt ?? string.Empty).Length);
                if (decision != null)
                {
                    sb.AppendLine("RagMode: " + decision.Mode);
                    sb.AppendLine("Top1Score: " + decision.Top1Score.ToString("0.000"));
                    sb.AppendLine("Top2Score: " + decision.Top2Score.ToString("0.000"));
                    sb.AppendLine("Confidence: " + decision.Confidence.ToString("0.000"));
                }
                sb.AppendLine();

                sb.AppendLine("## Strict / Mode Block");
                sb.AppendLine(strictRagBlock ?? string.Empty);
                sb.AppendLine();

                sb.AppendLine("## Direct Answer Block");
                sb.AppendLine(directAnswerBlock ?? string.Empty);
                sb.AppendLine();

                sb.AppendLine("## RAG Context");
                sb.AppendLine(ragContext ?? string.Empty);
                sb.AppendLine();

                sb.AppendLine("## Memory Context");
                sb.AppendLine(memoryContext ?? string.Empty);
                sb.AppendLine();

                sb.AppendLine("## Reflection Context");
                sb.AppendLine(lessonsContext ?? string.Empty);
                sb.AppendLine();

                sb.AppendLine("## Personality Block");
                sb.AppendLine(personalityBlock ?? string.Empty);
                sb.AppendLine();

                sb.AppendLine("## Personality Traits Block");
                sb.AppendLine(traitsContext ?? string.Empty);
                sb.AppendLine();

                sb.AppendLine("## Knowledge Profile");
                sb.AppendLine(knowledgeContext ?? string.Empty);
                sb.AppendLine();

                sb.AppendLine("## Emotion Block");
                sb.AppendLine(emotionContext ?? string.Empty);
                sb.AppendLine();

                sb.AppendLine("## Experience Block");
                sb.AppendLine(experienceContext ?? string.Empty);

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // ignore logging failures
            }
        }

        public ChatViewState BuildViewState(OllamaStatus ollamaStatus = null)
        {
            var count = _trainingExportService.GetExampleCount();
            return new ChatViewState
            {
                VisibleMessages = _conversationService.CurrentMessages
                    .Where(m => m.Role != ChatRole.System)
                    .ToList(),
                UserDisplayName = _conversationService.GetUserDisplayName(),
                AssistantName = _conversationService.AssistantName,
                TrainingExampleCount = count,
                TrainingHint = _trainingExportService.GetTrainingHint(count),
                MemoryCount = _memoryService.GetMemoryCount(),
                KnowledgeChunkCount = _ragService.GetChunkCount(),
                OllamaStatus = ollamaStatus
            };
        }

        private sealed class PromptBuildResult
        {
            public IList<ChatMessage> Messages { get; set; }
            public RagPriorityEvaluation RagEvaluation { get; set; }
            public string RagContext { get; set; }
            public int SystemPromptChars { get; set; }
            public SelfCheckContext SelfCheckContext { get; set; }
            public string MemoryContext { get; set; }
            public DateTimeContext DateTimeContext { get; set; }
        }
    }
}
