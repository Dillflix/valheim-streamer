using System;
using System.Collections.Generic;
namespace ValheimBroadcaster.Core
{
    /// <summary>The game removes every scene instance not in either retained-object list.
    /// A reference override must abort before removal if its observer would be excluded.</summary>
    public static class SceneRetention
    {
        public static bool MustAbort<T>(bool overriding, T? observer, ICollection<T> near, ICollection<T> distant) where T : class
        {
            if (!overriding) return false;
            if (near == null || distant == null) throw new ArgumentNullException();
            return observer == null || (!near.Contains(observer) && !distant.Contains(observer));
        }
    }
}
