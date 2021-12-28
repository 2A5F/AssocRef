using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volight.AssocRefs;

namespace Test
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Test1()
        {
            var map = new AssocMap<int, int>();
            for (int i = 0; i < 10; i++)
            {
                int v = 0;
                Task.WaitAll(Enumerable.Range(0, 100).Select(i => Task.Run(() =>
                {
                    using var a = map.GetOrAdd(0, a => Interlocked.Increment(ref v));
                    Assert.NotZero(map.Count);
                })).ToArray());
            }
            Assert.Zero(map.Count);
        }
    }
}