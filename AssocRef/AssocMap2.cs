using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Volight.AssocRefs;

class AssocMap2<K, V> where K : notnull
{
    // Basically copied from dotnet/runtime/src/libraries/System.Collections.Concurrent/src/System/Collections/Concurrent/ConcurrentDictionary.cs

    volatile Tables tables;
    readonly IEqualityComparer<K>? comparer;
    readonly EqualityComparer<K> defaultComparer;
    readonly bool growLockArray;
    int budget;

    const int DefaultCapacity = 31;
    const int MaxLockNumber = 1024;

    static int DefaultConcurrencyLevel => Environment.ProcessorCount;

    public AssocMap2() : this(DefaultConcurrencyLevel, DefaultCapacity, growLockArray: true, null) { }

    internal AssocMap2(int concurrencyLevel, int capacity) : this(concurrencyLevel, capacity, growLockArray: false, null) { }

    internal AssocMap2(IEqualityComparer<K>? comparer) : this(DefaultConcurrencyLevel, DefaultCapacity, growLockArray: true, comparer) { }

    internal AssocMap2(int concurrencyLevel, int capacity, IEqualityComparer<K>? comparer) : this(concurrencyLevel, capacity, growLockArray: false, comparer) { }

    internal AssocMap2(int concurrencyLevel, int capacity, bool growLockArray, IEqualityComparer<K>? comparer)
    {
        if (concurrencyLevel < 1) throw new ArgumentOutOfRangeException(nameof(concurrencyLevel));
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));

        if (capacity < concurrencyLevel) capacity = concurrencyLevel;

        var locks = new ReaderWriterLockSlim[concurrencyLevel];
        for (int i = 0; i < locks.Length; i++) locks[i] = new ReaderWriterLockSlim();

        var countPerLock = new int[locks.Length];
        var buckets = new Node[capacity];
        tables = new Tables(buckets, locks, countPerLock);

        defaultComparer = EqualityComparer<K>.Default;
        if (comparer != null && !ReferenceEquals(comparer, defaultComparer) && !ReferenceEquals(comparer, StringComparer.Ordinal)) this.comparer = comparer;

        this.growLockArray = growLockArray;
        budget = buckets.Length / locks.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    int GetHashCode(K key) => comparer == null ? GetHashCode(key) : comparer.GetHashCode(key);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool ComparerEquals(K a, K b) => comparer is null ? defaultComparer.Equals(a, b) : comparer.Equals(a, b);

    public AssocRef<V> GetOrAdd(K key, V value)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));

        var hashcode = GetHashCode(key);

        if (!DoGet(key, hashcode, out var refv))
        {
            DoAdd(key, hashcode, value, true, out refv);
        }

        return refv;
    }

    public AssocRef<V> GetOrAdd(K key, Func<K, V> valueFactory)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (valueFactory is null) throw new ArgumentNullException(nameof(valueFactory));

        var hashcode = GetHashCode(key);

        if (!DoGet(key, hashcode, out var refv))
        {
            DoAdd(key, hashcode, valueFactory(key), true, out refv);
        }

        return refv;
    }

    internal bool DoAdd(K key, int hashcode, V value, bool acquireLock, out AssocRef<V> resultingValue)
    {
        for (; ; )
        {
            var tables = this.tables;
            var locks = tables.locks;
            ref var bucket = ref tables.GetBucketAndLock(hashcode, out var lockNo);

            var resizeDesired = false;
            var lockTaken = false;
            var lockTaken2 = false;

            try
            {
                lockTaken = locks[lockNo].TryEnterReadLock(-1);
                if (acquireLock) Monitor.Enter(locks[lockNo], ref lockTaken2);

                // If the table just got resized, we may not be holding the right lock, and must retry.
                // This should be a rare occurrence.
                if (tables != this.tables) continue;

                // Try to find this key in the bucket
                Node? prev = null;
                for (Node? node = bucket; node != null; node = node.next)
                {
                    Debug.Assert((prev is null && node == bucket) || prev!.next == node);
                    if (hashcode == node.hashcode && ComparerEquals(node.key, key))
                    {
                        resultingValue = new(node.Inc());
                        return false;
                    }
                    prev = node;
                }

                var resultNode = new Node(this, key, value, hashcode, bucket);
                Volatile.Write(ref bucket, resultNode);
                checked
                {
                    tables.countPerLock[lockNo]++;
                }

                //
                // If the number of elements guarded by this lock has exceeded the budget, resize the bucket table.
                // It is also possible that GrowTable will increase the budget but won't resize the bucket table.
                // That happens if the bucket table is found to be poorly utilized due to a bad hash function.
                //
                if (tables.countPerLock[lockNo] > budget) resizeDesired = true;

                resultingValue = new(resultNode);
            }
            finally
            {
                if (lockTaken) locks[lockNo].ExitReadLock();
                if (lockTaken2) Monitor.Exit(locks[lockNo]);
            }

            //
            // The fact that we got here means that we just performed an insertion. If necessary, we will grow the table.
            //
            // Concurrency notes:
            // - Notice that we are not holding any locks at when calling GrowTable. This is necessary to prevent deadlocks.
            // - As a result, it is possible that GrowTable will be called unnecessarily. But, GrowTable will obtain lock 0
            //   and then verify that the table we passed to it as the argument is still the current table.
            //
            if (resizeDesired)
            {
                GrowTable(tables);
            }
            return true;
        }
    }

    internal bool DoGet(K key, int hashcode, [MaybeNullWhen(false)] out AssocRef<V> value)
    {
        var tables = this.tables;
        var locks = tables.locks;
        ref var bucket = ref tables.GetBucketAndLock(hashcode, out var lockNo);

        var lockTaken = false;

        try
        {
            lockTaken = locks[lockNo].TryEnterReadLock(-1);

            if (comparer is null)
            {
                if (typeof(K).IsValueType)
                {
                    for (Node? n = Volatile.Read(ref tables.GetBucket(hashcode)); n != null; n = n.next)
                    {
                        if (n.count != 0 && hashcode == n.hashcode && EqualityComparer<K>.Default.Equals(n.key, key))
                        {
                            value = new(n.Inc());
                            return true;
                        }
                    }
                }
                else
                {
                    for (Node? n = Volatile.Read(ref tables.GetBucket(hashcode)); n != null; n = n.next)
                    {
                        if (n.count != 0 && hashcode == n.hashcode && defaultComparer.Equals(n.key, key))
                        {
                            value = new(n.Inc());
                            return true;
                        }
                    }
                }
            }
            else
            {
                for (Node? n = Volatile.Read(ref tables.GetBucket(hashcode)); n != null; n = n.next)
                {
                    if (n.count != 0 && hashcode == n.hashcode && comparer.Equals(n.key, key))
                    {
                        value = new(n.Inc());
                        return true;
                    }
                }
            }
        }
        finally
        {
            if (lockTaken) locks[lockNo].ExitReadLock();
        }

        value = null;
        return false;
    }

    internal bool DoRemove(K key)
    {
        var hashcode = GetHashCode(key);
        for (; ; )
        {
            var tables = this.tables;
            var locks = tables.locks;
            ref var bucket = ref tables.GetBucketAndLock(hashcode, out var lockNo);

            var lockTaken = false;

            try
            {
                lockTaken = locks[lockNo].TryEnterWriteLock(-1);
                lock (locks[lockNo])
                {
                    // If the table just got resized, we may not be holding the right lock, and must retry.
                    // This should be a rare occurrence.
                    if (tables != this.tables) continue;

                    Node? prev = null;
                    for (Node? curr = bucket; curr != null; curr = curr.next)
                    {
                        Debug.Assert((prev is null && curr == bucket) || prev!.next == curr);
                        if (hashcode == curr.hashcode && ComparerEquals(curr.key, key))
                        {
                            if (prev is null)
                            {
                                Volatile.Write(ref bucket, curr.next);
                            }
                            else
                            {
                                prev.next = curr.next;
                            }


                            tables.countPerLock[lockNo]--;
                            return true;
                        }
                        prev = curr;
                    }

                    return false;
                }
            }
            finally
            {
                if (lockTaken) locks[lockNo].ExitWriteLock();
            }
            
        }
    }

    void GrowTable(Tables tables)
    {

    }

    sealed class Node : IRefCount<V>
    {
        readonly AssocMap2<K, V> map;
        public readonly K key;
        public V value;
        public volatile Node? next;
        public readonly int hashcode;
        public volatile uint count = 0;

        public V Value { get => value; set => this.value = value; }

        public Node(AssocMap2<K, V> map, K key, V value, int hashcode, Node? next)
        {
            this.map = map;
            this.key = key;
            this.value = value;
            this.hashcode = hashcode;
            this.next = next;
            count = 1;
        }

        public Node Inc()
        {
            Interlocked.Increment(ref count);
            return this;
        }

        public void Drop()
        {
            if (checked(Interlocked.Decrement(ref count) == 0))
            {
                map.DoRemove(key);
            }
        }
    }

    sealed class Tables
    {
        public readonly Node?[] buckets;
        public readonly ReaderWriterLockSlim[] locks;
        public readonly int[] countPerLock;
        public readonly ulong fastModBucketsMultiplier;

        static ulong GetFastModMultiplier(uint divisor) => ulong.MaxValue / divisor + 1UL;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint FastMod(uint value, uint divisor, ulong multiplier) => (uint)(((multiplier * value >> 32) + 1UL) * divisor >> 32);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint SlowMod(uint hashcode, uint length) => hashcode % length;

        public Tables(Node?[] buckets, ReaderWriterLockSlim[] locks, int[] countPerLock)
        {
            this.buckets = buckets;
            this.locks = locks;
            this.countPerLock = countPerLock;
            if (IntPtr.Size == 8)
            {
                fastModBucketsMultiplier = GetFastModMultiplier((uint)buckets.Length);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetBucketNo(int hashcode) => IntPtr.Size == 8
            ? FastMod((uint)hashcode, (uint)buckets.Length, fastModBucketsMultiplier)
            : SlowMod((uint)hashcode, (uint)buckets.Length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref Node? GetBucket(int hashcode) => ref buckets[GetBucketNo(hashcode)];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref Node? GetBucketAndLock(int hashcode, out uint lockNo)
        {
            var bucketNo = GetBucketNo(hashcode);
            lockNo = SlowMod(bucketNo, (uint)locks.Length);
            return ref buckets[bucketNo];
        }
    }

}
