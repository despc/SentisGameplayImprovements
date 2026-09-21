using System;

namespace SentisGameplayImprovements.Loot
{
    /// <summary>
    /// How many components a hit knocks out of a block - the arithmetic only, so it can be tested.
    /// </summary>
    public static class LootMath
    {
        /// <summary>One component stack of a block as it stands before the hit.</summary>
        public struct Stack
        {
            public float Integrity;     // what the stack has now
            public int Mounted;         // components in place
            public int Total;           // components the full stack has
            public float MaxIntegrity;  // what the full stack has

            public Stack(float integrity, int mounted, int total, float maxIntegrity)
            {
                Integrity = integrity;
                Mounted = mounted;
                Total = total;
                MaxIntegrity = maxIntegrity;
            }
        }

        /// <summary>
        /// Components lost from each stack. Damage eats a block from its last stack down, like the
        /// game's own damage does: a stack gives up its integrity, and only what is left of the hit
        /// goes on to the stack below. A stack keeps as many components as its remaining integrity
        /// needs - a component that is merely scratched stays.
        ///
        /// The old count, (int)((integrity - damage) / perComponent) + 1, kept one component too
        /// many whenever the rest came out a whole number of components, and one too few on a
        /// stack the hit had just emptied.
        /// </summary>
        public static int[] Lost(Stack[] stacks, float damage)
        {
            var lost = new int[stacks.Length];
            if (damage <= 0) return lost;
            for (var i = stacks.Length - 1; i >= 0 && damage > 0; i--)
            {
                var stack = stacks[i];
                if (stack.Mounted <= 0 || stack.Total <= 0 || stack.MaxIntegrity <= 0) continue;
                var perComponent = stack.MaxIntegrity / stack.Total;
                var taken = Math.Min(damage, Math.Max(0f, stack.Integrity));
                var left = stack.Integrity - taken;
                // a hair under a whole component still needs that component
                var kept = (int)Math.Ceiling(left / perComponent - 1e-4);
                kept = Math.Max(0, Math.Min(stack.Mounted, kept));
                lost[i] = stack.Mounted - kept;
                damage -= taken;
            }
            return lost;
        }

        /// <summary>How many of the lost components come out whole: each one with the component's drop chance.</summary>
        public static int Dropped(int lost, float dropProbability) =>
            lost <= 0 || dropProbability <= 0 ? 0 : (int)(lost * Math.Min(1f, dropProbability));
    }
}
