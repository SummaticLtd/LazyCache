using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NUnit.Framework;

namespace LazyCache.UnitTests
{
    [TestFixture]
    public class CachingServiceMemoryCacheProviderTests
    {
        private CachingService sut;

        private static MemoryCacheEntryOptions Policy => new MemoryCacheEntryOptions();

        private ComplexTestObject testObject = new ComplexTestObject();

        private class ComplexTestObject
        {
            public readonly IList<object> SomeItems = new List<object> { 1, 2, 3, "testing123" };
            public string SomeMessage = "testing123";
        }

        private const string TestKey = "testKey";

        [SetUp]
        public void BeforeEachTest()
        {
            sut = new CachingService();
            testObject = new ComplexTestObject();
        }

        [Test]
        public void AddNullThrowsException()
        {
            Action act = () => sut.Add<object>(TestKey, null, Policy);
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void AddThenGetReturnsCachedObject()
        {
            sut.Add(TestKey, "testObject", Policy);
            sut.TryGetValue<string>(TestKey, out var actual).Should().BeTrue();
            Assert.AreEqual("testObject", actual);
        }

        [Test]
        public void AddWithOffsetThatExpiresReturnsNull()
        {
            sut.Add(TestKey, "testObject", new MemoryCacheEntryOptions { AbsoluteExpiration = DateTimeOffset.Now.AddSeconds(1) });
            Thread.Sleep(1500);
            sut.TryGetValue<string>(TestKey, out _).Should().BeFalse();
        }

        [Test]
        public void RemovedItemCannotBeRetrievedFromCache()
        {
            sut.Add(TestKey, new object(), Policy);
            sut.TryGetValue<object>(TestKey, out _).Should().BeTrue();
            sut.Remove(TestKey);
            sut.TryGetValue<object>(TestKey, out _).Should().BeFalse();
        }

        [Test]
        public void TryGetReturnsCachedValueAndTrue()
        {
            string val = "Test Value";
            string key = "testkey";
            sut.Add(key, val, Policy);

            var contains = sut.TryGetValue<string>(key, out var value);

            Assert.IsTrue(contains);
            Assert.AreEqual(value, val);

            var contains2 = sut.TryGetValue<string>("invalidkey", out var value2);

            Assert.IsFalse(contains2);

            Assert.Throws<InvalidCastException>(() => sut.TryGetValue<int>(key, out var value3));
        }

        [Test]
        public void GetFromCacheTwiceAtSameTimeOnlyAddsOnce()
        {
            var times = 0;

            var t1 = Task.Factory.StartNew(() =>
            {
                sut.GetOrAdd(TestKey, _ =>
                {
                    Interlocked.Increment(ref times);
                    return new DateTime(2001, 01, 01);
                }, Policy);
            });

            var t2 = Task.Factory.StartNew(() =>
            {
                sut.GetOrAdd(TestKey, _ =>
                {
                    Interlocked.Increment(ref times);
                    return new DateTime(2001, 01, 01);
                }, Policy);
            });

            Task.WaitAll(t1, t2);

            Assert.AreEqual(1, times);
        }

        [Test]
        public void GetOrAddWillAddOnFirstCallButReturnCachedOnSecond()
        {
            var times = 0;

            var expectedFirst = sut.GetOrAdd(TestKey, _ =>
            {
                times++;
                return new DateTime(2001, 01, 01);
            }, Policy);

            var expectedSecond = sut.GetOrAdd(TestKey, _ =>
            {
                times++;
                return new DateTime(2002, 01, 01);
            }, Policy);

            Assert.AreEqual(2001, expectedFirst.Year);
            Assert.AreEqual(2001, expectedSecond.Year);
            Assert.AreEqual(1, times);
        }

        [Test]
        public void GetOrAddAFailingFactoryDoesNotCacheIt()
        {
            Action act = () => sut.GetOrAdd<ComplexTestObject>(TestKey, _ => throw new ApplicationException(), Policy);
            act.Should().Throw<ApplicationException>();

            var actual = sut.GetOrAdd(TestKey, _ => testObject, Policy);

            Assert.That(actual, Is.SameAs(testObject));
        }

        [Test]
        public void GetOrAddWithAbsoluteOffsetExpiryAsTimeSpanDoesExpireItems()
        {
            var millisecondsCacheDuration = 100;
            var validResult = sut.GetOrAdd(
                TestKey,
                _ => new ComplexTestObject(),
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(millisecondsCacheDuration) }
            );
            // pass expiry time with a delay
            Thread.Sleep(TimeSpan.FromMilliseconds(millisecondsCacheDuration + 50));
            var expiredResult = sut.GetOrAdd(TestKey, _ => testObject, Policy);

            Assert.That(validResult, Is.Not.Null);
            Assert.That(expiredResult, Is.SameAs(testObject));
        }

        [Test]
        public void GetOrAddWithAbsoluteOffsetExpiryInTheDelegateUsingTimeSpanDoesExpireItems()
        {
            var millisecondsCacheDuration = 100;
            var validResult = sut.GetOrAdd(
                TestKey,
                entry =>
                {
                    entry.SetAbsoluteExpiration(TimeSpan.FromMilliseconds(millisecondsCacheDuration));
                    return new ComplexTestObject();
                },
                Policy
            );
            // pass expiry time with a delay
            Thread.Sleep(TimeSpan.FromMilliseconds(millisecondsCacheDuration + 50));
            var expiredResult = sut.GetOrAdd(TestKey, _ => testObject, Policy);

            Assert.That(validResult, Is.Not.Null);
            Assert.That(expiredResult, Is.SameAs(testObject));
        }

        [Test]
        public async Task GetOrAddAsyncTwiceAtSameTimeOnlyAddsOnce()
        {
            var times = 0;

            async Task<DateTime> FetchAsync(ICacheEntry _)
            {
                Interlocked.Increment(ref times);
                await Task.Delay(50);
                return new DateTime(2001, 01, 01);
            }

            var t1 = Task.Run(() => sut.GetOrAddAsync(TestKey, FetchAsync, Policy));
            var t2 = Task.Run(() => sut.GetOrAddAsync(TestKey, FetchAsync, Policy));

            await Task.WhenAll(t1, t2);

            Assert.AreEqual(1, times);
        }

        [Test]
        public async Task GetOrAddAsyncACancelledTaskDoesNotCacheIt()
        {
            Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await sut.GetOrAddAsync(TestKey, _ => AsyncHelper.CreateCancelledTask<ComplexTestObject>(), Policy));

            var actual = await sut.GetOrAddAsync(TestKey, _ => Task.FromResult(testObject), Policy);

            Assert.That(actual, Is.SameAs(testObject));
        }

        [Test]
        public void GetOrAddAsyncACancelledTaskReturnsTheCacelledTaskToConsumer()
        {
            var cancelledTask = sut.GetOrAddAsync(TestKey, _ => AsyncHelper.CreateCancelledTask<ComplexTestObject>(), Policy);

            Assert.That(cancelledTask, Is.Not.Null);

            Assert.Throws<AggregateException>(cancelledTask.Wait);

            Assert.That(cancelledTask.IsCanceled, Is.True);
        }

        [Test]
        public async Task GetOrAddAsyncAFailingTaskDoesNotCacheIt()
        {
            Task<ComplexTestObject> FetchAsync(ICacheEntry _)
            {
                return Task<ComplexTestObject>.Factory.StartNew(() => throw new ApplicationException());
            }

            Assert.ThrowsAsync<ApplicationException>(async () => await sut.GetOrAddAsync(TestKey, FetchAsync, Policy));

            var actual = await sut.GetOrAddAsync(TestKey, _ => Task.FromResult(testObject), Policy);

            Assert.That(actual, Is.SameAs(testObject));
        }

        [Test]
        public async Task GetOrAddAsyncWillAddOnFirstCallButReturnCachedOnSecond()
        {
            var times = 0;

            var expectedFirst = await sut.GetOrAddAsync(TestKey, _ =>
            {
                times++;
                return Task.FromResult(new DateTime(2001, 01, 01));
            }, Policy);

            var expectedSecond = await sut.GetOrAddAsync(TestKey, _ =>
            {
                times++;
                return Task.FromResult(new DateTime(2002, 01, 01));
            }, Policy);

            Assert.AreEqual(2001, expectedFirst.Year);
            Assert.AreEqual(2001, expectedSecond.Year);
            Assert.AreEqual(1, times);
        }

        [Test]
        [MaxTime(1000)]
        public void GetOrAddAsyncWithALongTaskReturnsBeforeTaskCompletes()
        {
            var cachedResult = testObject;

            Task<ComplexTestObject> FetchAsync(ICacheEntry _)
            {
                return Task.Delay(TimeSpan.FromMinutes(1))
                    .ContinueWith(x => cachedResult);
            }

            var actualResult = sut.GetOrAddAsync(TestKey, FetchAsync, Policy);

            Assert.That(actualResult, Is.Not.Null);
            Assert.That(actualResult.IsCompleted, Is.Not.True);
        }

        [Test]
        public async Task GetOrAddAsyncWithAbsoluteOffsetExpiryAsTimeSpanDoesExpireItems()
        {
            var millisecondsCacheDuration = 100;
            var validResult = await sut.GetOrAddAsync(
                TestKey,
                _ => Task.FromResult(new ComplexTestObject()),
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(millisecondsCacheDuration) }
            );
            // pass expiry time with a delay
            Thread.Sleep(TimeSpan.FromMilliseconds(millisecondsCacheDuration + 50));
            var expiredResult = await sut.GetOrAddAsync(TestKey, _ => Task.FromResult(testObject), Policy);

            Assert.That(validResult, Is.Not.Null);
            Assert.That(expiredResult, Is.SameAs(testObject));
        }

        [Test]
        public async Task GetOrAddAsyncWithAbsoluteOffsetExpiryInTheDelegateUsingTimeSpanAfterLongRunningTaskDoesExpireItems()
        {
            var millisecondsCacheDuration = 100;
            var validResult = await sut.GetOrAddAsync(
                TestKey,
                async entry =>
                {
                    await Task.Delay(50);
                    entry.SetAbsoluteExpiration(TimeSpan.FromMilliseconds(millisecondsCacheDuration));
                    return new ComplexTestObject();
                },
                Policy
            );
            // pass expiry time with a delay
            Thread.Sleep(TimeSpan.FromMilliseconds(millisecondsCacheDuration + 50));
            var expiredResult = await sut.GetOrAddAsync(TestKey, _ => Task.FromResult(testObject), Policy);

            Assert.That(validResult, Is.Not.Null);
            Assert.That(expiredResult, Is.SameAs(testObject));
        }
    }
}
