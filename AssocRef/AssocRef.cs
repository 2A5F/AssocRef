using System;
using System.Collections.Generic;

namespace AssocRefs;

public sealed class AssocRef<T> : IDisposable, ICloneable, IEquatable<AssocRef<T>>, IEquatable<T>
{
    readonly IRefCount<T> rc;
    volatile bool droped = false;

    internal AssocRef(IRefCount<T> rc)
    {
        this.rc = rc;
    }

    public T Value { get => rc.Value; set => rc.Value = value; }

    public T Swap(T newValue)
    {
        var oldValue = rc.Value;
        rc.Value = newValue;
        return oldValue;
    }

    ~AssocRef() => Dispose();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (droped) return;
        droped = true;
        rc.Drop();
    }

    public bool Equals(T? other) => EqualityComparer<T>.Default.Equals(Value, other);

    public bool Equals(AssocRef<T>? other) => EqualityComparer<IRefCount<T>>.Default.Equals(rc, other?.rc);

    public override bool Equals(object? obj) => Equals(obj as AssocRef<T>);

    public override int GetHashCode() => rc.GetHashCode();

    public static implicit operator T(AssocRef<T> self) => self.Value;

    public static bool operator ==(AssocRef<T> left, AssocRef<T> right) => EqualityComparer<AssocRef<T>>.Default.Equals(left, right);

    public static bool operator !=(AssocRef<T> left, AssocRef<T> right) => !(left == right);

    public AssocRef<T> Clone() => new(rc);

    object ICloneable.Clone() => Clone();
}
