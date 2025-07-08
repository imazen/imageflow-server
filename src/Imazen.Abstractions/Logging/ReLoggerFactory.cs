using Microsoft.Extensions.Logging;

namespace Imazen.Abstractions.Logging
{
    public sealed class ReLoggerFactory : IReLoggerFactory
    {
        private readonly ILoggerFactory impl; 
        private readonly IReLogStore store;
        
        public ReLoggerFactory(ILoggerFactory impl, IReLogStore store)
        {
            this.impl = impl;
            this.store = store;
        }
        public void Dispose()
        {
            impl.Dispose();
        }

        public ILogger CreateLogger(string categoryName) => CreateReLogger(categoryName);
        public IReLogger CreateReLogger(string categoryName)
        {
            return new ReLogger(impl.CreateLogger(categoryName), this, categoryName);
        }
        public void AddProvider(ILoggerProvider provider)
        {
            impl.AddProvider(provider);
        }
        
        internal void Log<TState>(string categoryName, KeyValuePair<string, object>[]? reScopeData, LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter, bool retain, string? retainUniqueKey)
        {
            store.Log(categoryName, reScopeData, scopes.Value, logLevel, eventId, state, exception, formatter, retain, retainUniqueKey);
        }

        //  AsyncLocal is thread-local style storage that is async-aware, and is used by the official implementation 
        // for tracking BeginScope, so it's certainly fine
        private readonly AsyncLocal<Stack<IDisposable>> scopes = new AsyncLocal<Stack<IDisposable>>();
        internal void BeginScope<TState>(TState state, ReLoggerScope<TState> instance) where TState : notnull
        {
            scopes.Value ??= new Stack<IDisposable>(4);
            scopes.Value.Push(instance);
        }

        internal void EndScope<TState>(TState state, ReLoggerScope<TState> scope) where TState : notnull
        {
            if (scopes.Value == null) return;
            if (scopes.Value.Count == 0) return;
            if (ReferenceEquals(scopes.Value.Peek(), scope))
            {
                scopes.Value.Pop();
            }
        }


    }
}