using System;
using System.Collections.Generic;

namespace AugurACT.Core.Mechanics
{
    // The user's own order of a list in the encounters data (event texts, buffs and detrimentals): entries stay
    // where they were added, and the page's up / down arrows move them one place at a time.
    public static class ListOrder
    {
        // Moves the entry that matches by offset places (-1 is up), stopping at either end. False when it didn't move.
        public static bool Move<T>(List<T> list, Predicate<T> match, int offset)
        {
            var from = list.FindIndex(match);
            if (from < 0)
            {
                throw new ArgumentException("That entry isn't in the list any more.");
            }

            var to = Math.Max(0, Math.Min(list.Count - 1, from + offset));
            if (to == from)
            {
                return false;
            }

            var item = list[from];
            list.RemoveAt(from);
            list.Insert(to, item);
            return true;
        }
    }
}
