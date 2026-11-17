using System;
using System.Runtime.CompilerServices;

namespace LazyCache
{
    /// <summary>
    ///     A thread-safe lazy value like <see cref="Lazy{T}"/>, without its public parameterless constructor
    ///     requirement on T, which makes <see cref="Lazy{T}"/> trim and AOT unsafe for an unannotated T.
    /// </summary>
    public class SyncLazy<T>
    {
        private readonly Lazy<StrongBox<T>> lazy;

        public SyncLazy(Func<T> valueFactory)
        {
            lazy = new Lazy<StrongBox<T>>(() => new StrongBox<T>(valueFactory()));
        }

        public T Value => lazy.Value.Value;

        public bool IsValueCreated => lazy.IsValueCreated;
    }
}
