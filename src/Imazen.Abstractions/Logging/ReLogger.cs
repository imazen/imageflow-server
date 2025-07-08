using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Imazen.Abstractions.Logging
{
    internal class ReLogger : IReLogger
    {
        private readonly ILogger impl;
        private readonly bool retain;
        private readonly string? retainUniqueKey;
        private readonly ReLoggerFactory parent;
        private readonly string categoryName;

        private readonly KeyValuePair<string, object>[]? reScopeData;


        internal ReLogger(ILogger impl, ReLoggerFactory parent, string categoryName)
        {
            this.impl = impl;
            this.parent = parent;
            this.retain = false;
            this.retainUniqueKey = null;
            this.categoryName = categoryName;
        }
        private ReLogger(ILogger impl, ReLoggerFactory parent, string categoryName, bool retain, string? retainUniqueKey, KeyValuePair<string, object>[]? reScopeData)
        {
            this.impl = impl;
            this.retain = retain;
            this.parent = parent;
            this.retainUniqueKey = retainUniqueKey;
            this.categoryName = categoryName;
            this.reScopeData = reScopeData;
        }
        
        internal bool SharesParentWith(ReLogger? other)
        {
            return parent == other?.parent;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            
            parent.Log(categoryName, reScopeData, logLevel, eventId, state, exception, formatter, retain, retainUniqueKey);
            if (reScopeData != null){
                using (impl.BeginScope(reScopeData))
                {
                    impl.Log(logLevel, eventId, state, exception, formatter);
                }
            }
            else
            {
                impl.Log(logLevel, eventId, state, exception, formatter);
            }
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return impl.IsEnabled(logLevel);
        }

 
        private IReLogger? withRetain = null;
        public IReLogger WithRetain
        {
            get
            {
                withRetain ??= new ReLogger(impl, parent, categoryName, true, null, reScopeData);
                return withRetain;
            }
        }

        public IReLogger WithRetainUnique(string key)
        {
            return new ReLogger(impl, parent, categoryName, true, key, reScopeData);
        }
        
        public IReLogger WithSubcategory(string subcategoryString)
        {
            var newCategoryName = $"{categoryName} > {subcategoryString}";
            return new ReLogger(parent.CreateLogger(newCategoryName), parent, newCategoryName, retain, retainUniqueKey, reScopeData);
        }

        public IReLogger WithReScopeData(string key, object value)
        {
            var newScopeData = new KeyValuePair<string, object>[(reScopeData?.Length ?? 0) + 1];
            if (reScopeData != null) Array.Copy(reScopeData, newScopeData, reScopeData.Length);
            newScopeData[^1] = new KeyValuePair<string, object>(key, value);

            return new ReLogger(impl, parent, categoryName, retain, retainUniqueKey, newScopeData);
        }
        public IReLogger WithReScopeData(KeyValuePair<string, object>[] pairs)
        {
            var newScopeData = new KeyValuePair<string, object>[(reScopeData?.Length ?? 0) + pairs.Length];
            if (reScopeData != null) Array.Copy(reScopeData, newScopeData, reScopeData.Length);
            Array.Copy(pairs, 0, newScopeData, newScopeData.Length - pairs.Length, pairs.Length);
            return new ReLogger(impl, parent, categoryName, retain, retainUniqueKey, newScopeData);
        }
        
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            var implScope = impl.BeginScope(state);
            if (implScope == null) return null;
            var instance = new ReLoggerScope<TState>(implScope, state, this);
            parent.BeginScope(state, instance);
            return instance;
        }
        internal void EndScope<TState>(TState state, ReLoggerScope<TState> scope) where TState : notnull
        {
            parent.EndScope(state, scope);
        }
    }

    internal class ReLogger<T> : IReLogger<T> {
        private readonly IReLogger impl;
        public ReLogger(IReLoggerFactory factory) {
            impl = factory.CreateReLogger(typeof(T).FullName ?? typeof(T).Name);
        }
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            impl.Log(logLevel, eventId, state, exception, formatter);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return impl.IsEnabled(logLevel);
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return impl.BeginScope(state);
        }
        public IReLogger WithRetain
        {
            get
            {
                return impl.WithRetain;
            }
        }
        public IReLogger WithRetainUnique(string key)
        {
            return impl.WithRetainUnique(key);
        }
        public IReLogger WithSubcategory(string subcategoryString)
        {
            return impl.WithSubcategory(subcategoryString);
        }
        public IReLogger WithReScopeData(string key, object value)
        {
            return impl.WithReScopeData(key, value);
        }
        public IReLogger WithReScopeData(KeyValuePair<string, object>[] pairs)
        {
            return impl.WithReScopeData(pairs);
        }
        
    }
}
