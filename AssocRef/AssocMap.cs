using System;
using System.Collections.Concurrent;

namespace Volight.AssocRefs;

public sealed class AssocMap<K, V> where K : notnull
{
    readonly ConcurrentDictionary<K, RefCount<K, V>> Map = new();

    public AssocRef<V> GetOrAdd(K key, Func<K, V> valueFactory) => new(Map.GetOrAdd(key, key => new RefCount<K, V>(this, key, valueFactory(key))));

    public AssocRef<V> GetOrAdd(K key, V value) => new(Map.GetOrAdd(key, key => new RefCount<K, V>(this, key, value)));

    public AssocRef<V> GetOrAdd<A>(K key, Func<K, A, V> valueFactory, A factoryArgument) => new(Map.GetOrAdd(key, key => new RefCount<K, V>(this, key, valueFactory(key, factoryArgument))));

    public AssocRef<V> AddOrUpdate(K key, Func<K, V> addValueFactory, Func<K, V, V> updateValueFactory) => new(Map.AddOrUpdate(key, key => new RefCount<K, V>(this, key, addValueFactory(key)), (key, rc) => rc.Set(updateValueFactory(key, rc.Value))));

    public AssocRef<V> AddOrUpdate(K key, V addValue, Func<K, V, V> updateValueFactory) => new(Map.AddOrUpdate(key, key => new RefCount<K, V>(this, key, addValue), (key, rc) => rc.Set(updateValueFactory(key, rc.Value))));

    public AssocRef<V> AddOrUpdate<A>(K key, Func<K, A, V> addValueFactory, Func<K, V, A, V> updateValueFactory, A factoryArgument) => new(Map.AddOrUpdate(key, key => new RefCount<K, V>(this, key, addValueFactory(key, factoryArgument)), (key, rc) => rc.Set(updateValueFactory(key, rc.Value, factoryArgument))));

    internal void Drop(RefCount<K, V> rc) => Map.TryRemove(rc.Key, out var _);
}
