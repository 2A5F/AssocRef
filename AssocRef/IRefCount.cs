using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssocRefs;

internal interface IRefCount<T>
{
    T Value { get; set; }

    void Drop();
}
