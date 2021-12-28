using System;
using System.Diagnostics;
using System.Threading;

namespace Volight.AssocRefs;

internal sealed class RefCount<K, V> : IRefCount<V> where K : notnull
{
    readonly AssocMap<K, V> Map;
    public readonly K Key;
    public V Value { get; set; }
    ulong count = 0;

    public RefCount<K, V> Set(V value)
    {
        Value = value;
        return this;
    }

    public RefCount(AssocMap<K, V> map, K key, V value)
    {
        Map = map;
        Key = key;
        Value = value;
    }

    public void Inc()
    {
        Interlocked.Increment(ref count);
    }

    public void Drop()
    {
        if (checked(Interlocked.Decrement(ref count) == 0))
        {
            Map.Drop(this);
        }
    }
}
