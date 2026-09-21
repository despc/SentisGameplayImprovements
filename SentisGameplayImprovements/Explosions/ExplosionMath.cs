using System;
using VRageMath;

namespace SentisGameplayImprovements.Explosions
{
    /// <summary>The arithmetic of an explosion, with no game types in it, so it can be tested.</summary>
    public static class ExplosionMath
    {
        /// <summary>
        /// The share of the damage still arriving that a block takes, by how far its centre is
        /// from the explosion and how far the damage already travelled (the game's own falloff).
        /// A block whose centre lies at or past the radius takes nothing: the blast only grazes its
        /// box. Without that rule, which the game has, such blocks took the whole damage left.
        /// </summary>
        public static float Falloff(float blockDistance, float travelled, float radius)
        {
            if (blockDistance >= radius) return 0f;
            if (radius - travelled <= 0f) return 1f;
            return MathHelper.Clamp(1f - (blockDistance - travelled) / (radius - travelled), 0f, 1f);
        }

        /// <summary>
        /// Whether a crate of ammunition or explosives shaken this hard goes off. The acceleration
        /// is a vector: the old test compared each axis to the limit on its positive side only, so
        /// a blow from the other side never counted.
        /// </summary>
        public static bool ShouldDetonate(Vector3 acceleration, float limit) =>
            limit > 0 && acceleration.LengthSquared() > limit * limit;

        /// <summary>
        /// One block on a ray from the explosion outwards: the damage it takes, and what goes on
        /// to the blocks behind it. The blast only goes deeper through a block it destroys, and
        /// then with what was left over after destroying it; a block that stands stops it.
        /// </summary>
        /// <param name="arriving">damage reaching the block along the ray</param>
        /// <param name="share">the falloff at the block (<see cref="Falloff"/>)</param>
        /// <param name="multiplier">the block's damage multiplier from its definition</param>
        /// <param name="toDestroy">the damage, as it will be applied, that destroys the block</param>
        /// <param name="dealt">the damage the block takes</param>
        /// <returns>the damage going on past the block</returns>
        public static float PassThrough(float arriving, float share, float multiplier, float toDestroy, out float dealt)
        {
            dealt = arriving > 0f && share > 0f && multiplier > 0f ? arriving * share * multiplier : 0f;
            if (dealt <= toDestroy) return 0f;
            return (dealt - Math.Max(0f, toDestroy)) / multiplier;
        }

        /// <summary>The damage and radius of a stack of ammunition going off, per round.</summary>
        public static float AmmoDamage(float perRound, float fallbackPerRound, float multiplier, int rounds) =>
            (perRound > 0 ? perRound : fallbackPerRound) * multiplier * Math.Max(0, rounds);
    }
}
