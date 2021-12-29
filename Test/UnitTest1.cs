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
            var map = new AssocMap2<int, int>();
            int v = 0;
            for (int i = 0; i < 100; i++)
            {
                Task.WaitAll(Enumerable.Range(0, 100).Select(i => Task.Run(() =>
                {
                    using var a = map.GetOrAdd(0, a => Interlocked.Increment(ref v));
                    Assert.NotZero(map.Count);
                })).ToArray());
            }
            Console.WriteLine(v);
            Assert.Zero(map.Count);
        }

        class Counter
        {
            public int v;
        }
        class DeObj
        {
            public int i;
            public Counter counter;

            ~DeObj()
            {
                Interlocked.Increment(ref counter.v);
            }
        }

        [Test]
        public void Test2()
        {
            var v = 0;
            var counter = new Counter();
            var map = new AssocMap2<int, DeObj>();
            for (int i = 0; i < 100; i++)
            {
                Task.WaitAll(Enumerable.Range(0, 100).Select(i => Task.Run(() =>
                {
                    map.GetOrAdd(0, a => new DeObj { i = Interlocked.Increment(ref v), counter = counter });
                    Assert.NotZero(map.Count);
                })).ToArray());
            }
            GC.Collect();
            Console.WriteLine(v);
            Console.WriteLine(counter.v);
            Assert.AreEqual(v - counter.v, map.Count);
        }
    }
}
