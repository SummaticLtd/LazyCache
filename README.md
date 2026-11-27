# LazyCache, cut down

A fork of [alastairtree/LazyCache](https://github.com/alastairtree/LazyCache) keeping only what Summatic uses: `CachingService` with `GetOrAdd`, `GetOrAddAsync`, `Add`, `TryGetValue` and `Remove` over a `MemoryCache`.

`GetOrAdd` and `GetOrAddAsync` run the factory for a key once, even under concurrent calls, and cache nothing if it fails. Each key must be used with one type, through only one of `Add`, `GetOrAdd` and `GetOrAddAsync`.

MIT licensed; see [LICENSE](LICENSE).
