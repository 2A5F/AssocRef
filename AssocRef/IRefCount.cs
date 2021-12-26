using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Volight.AssocRefs;

internal interface IRefCount<T>
{
    T Value { get; set; }

    void Inc();

    void Drop();
}
