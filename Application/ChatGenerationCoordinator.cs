using System;
using System.Threading;
using Koca_Kafa.Data.Abstractions;

namespace Koca_Kafa.Application
{
    public sealed class ChatGenerationCoordinator
    {
        private readonly object _sync = new object();
        private readonly IDataPathProvider _paths;
        private GenerationLease _current;

        public ChatGenerationCoordinator(IDataPathProvider paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public GenerationLease Begin(string triggerMessage)
        {
            if (string.IsNullOrWhiteSpace(triggerMessage))
                triggerMessage = "(empty)";

            lock (_sync)
            {
                if (_current != null)
                {
                    GenerationLog.Cancelled(
                        _paths,
                        _current.TriggerMessage,
                        "superseded by: " + triggerMessage);
                    _current.Cancel();
                }

                _current = new GenerationLease(this, triggerMessage);
                return _current;
            }
        }

        public void CancelCurrent(string reason)
        {
            lock (_sync)
            {
                if (_current == null || _current.IsCancellationRequested)
                    return;

                GenerationLog.Cancelled(_paths, _current.TriggerMessage, reason);
                _current.Cancel();
            }
        }

        internal void Release(GenerationLease lease)
        {
            if (lease == null)
                return;

            lock (_sync)
            {
                if (ReferenceEquals(_current, lease))
                    _current = null;
            }

            lease.DisposeCore();
        }
    }

    public sealed class GenerationLease : IDisposable
    {
        private readonly ChatGenerationCoordinator _owner;
        private readonly CancellationTokenSource _source;
        private bool _disposed;

        internal GenerationLease(ChatGenerationCoordinator owner, string triggerMessage)
        {
            _owner = owner;
            TriggerMessage = triggerMessage;
            _source = new CancellationTokenSource();
        }

        public CancellationToken Token => _source.Token;
        public string TriggerMessage { get; }
        public bool IsCancellationRequested => _source.IsCancellationRequested;

        internal void Cancel()
        {
            if (!_source.IsCancellationRequested)
                _source.Cancel();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _owner.Release(this);
        }

        internal void DisposeCore()
        {
            if (_disposed)
                return;

            _disposed = true;
            _source.Dispose();
        }
    }
}
