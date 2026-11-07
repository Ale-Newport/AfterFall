using System;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;

namespace Afterfall.Domain.Survival
{
    public sealed class SurvivalState
    {
        public float Health { get; set; } = 100f;
        public float MaxHealth { get; set; } = 100f;
        public float Stamina { get; set; } = 100f;
        public float MaxStamina { get; set; } = 100f;

        /// <summary>100 = fully fed, 0 = starving.</summary>
        public float Hunger { get; set; } = 85f;

        /// <summary>100 = fully hydrated, 0 = dehydrated.</summary>
        public float Thirst { get; set; } = 80f;

        /// <summary>Seconds since stamina was last spent (regen waits for a short delay).</summary>
        public float SinceExertion { get; set; } = 10f;

        public bool IsDead => Health <= 0f;

        public void ResetForRespawn()
        {
            Health = MaxHealth * 0.6f;
            Stamina = MaxStamina;
            Hunger = Math.Max(Hunger, 50f);
            Thirst = Math.Max(Thirst, 50f);
            SinceExertion = 10f;
        }
    }

    /// <summary>Balance parameters for the survival model (per game day/hour, scaled by day length).</summary>
    public sealed class SurvivalConfig
    {
        /// <summary>Game days for hunger to fall from 100 to 0 at drain multiplier 1.</summary>
        public float HungerDaysToEmpty { get; set; } = 2.2f;

        public float ThirstDaysToEmpty { get; set; } = 1.4f;
        public float SprintStaminaPerSecond { get; set; } = 13f;
        public float StaminaRegenPerSecond { get; set; } = 20f;
        public float StaminaRegenDelay { get; set; } = 0.9f;

        /// <summary>Health lost per game hour at 0 hunger.</summary>
        public float StarvingDamagePerHour { get; set; } = 8f;

        /// <summary>Health lost per game hour at 0 thirst.</summary>
        public float DehydrationDamagePerHour { get; set; } = 12f;

        public float HealthRegenPerHour { get; set; } = 4f;
        public float WellFedThreshold { get; set; } = 50f;
        public float LowThreshold { get; set; } = 20f;
    }

    public static class SurvivalRules
    {
        /// <summary>
        /// Advances needs by <paramref name="dt"/> simulation seconds. Returns health lost to
        /// starvation/dehydration this step (for feedback), never negative.
        /// </summary>
        public static float Tick(SurvivalState s, SurvivalConfig c, PlayerActivity activity, double dt, double secondsPerGameHour, float drainMultiplier)
        {
            if (s.IsDead || dt <= 0) return 0f;
            double secondsPerDay = secondsPerGameHour * 24.0;
            float exertion = activity.Sprinting ? 1.6f : activity.Moving ? 1.1f : 1f;

            s.Hunger = Clamp(s.Hunger - (float)(100.0 / (c.HungerDaysToEmpty * secondsPerDay) * dt) * drainMultiplier * exertion, 0f, 100f);
            s.Thirst = Clamp(s.Thirst - (float)(100.0 / (c.ThirstDaysToEmpty * secondsPerDay) * dt) * drainMultiplier * exertion, 0f, 100f);

            // Stamina
            if (activity.Sprinting && activity.Moving)
            {
                s.Stamina = Clamp(s.Stamina - c.SprintStaminaPerSecond * (float)dt, 0f, s.MaxStamina);
                s.SinceExertion = 0f;
            }
            else
            {
                s.SinceExertion += (float)dt;
                if (s.SinceExertion >= c.StaminaRegenDelay)
                {
                    float regen = c.StaminaRegenPerSecond;
                    if (s.Hunger < c.LowThreshold) regen *= 0.5f;
                    if (s.Thirst < c.LowThreshold) regen *= 0.5f;
                    s.Stamina = Clamp(s.Stamina + regen * (float)dt, 0f, s.MaxStamina);
                }
            }

            // Health
            float hours = (float)(dt / secondsPerGameHour);
            float damage = 0f;
            if (s.Hunger <= 0f) damage += c.StarvingDamagePerHour * hours;
            if (s.Thirst <= 0f) damage += c.DehydrationDamagePerHour * hours;
            if (damage > 0f)
            {
                s.Health = Clamp(s.Health - damage, 0f, s.MaxHealth);
            }
            else if (s.Hunger >= c.WellFedThreshold && s.Thirst >= c.WellFedThreshold)
            {
                s.Health = Clamp(s.Health + c.HealthRegenPerHour * hours, 0f, s.MaxHealth);
            }
            return damage;
        }

        public static bool TrySpendStamina(SurvivalState s, float amount)
        {
            if (s.IsDead) return false;
            if (s.Stamina < amount * 0.5f) return false; // allow finishing a swing while tired, not spamming at zero
            s.Stamina = Math.Max(0f, s.Stamina - amount);
            s.SinceExertion = 0f;
            return true;
        }

        public static void Apply(SurvivalState s, ConsumableEffect e)
        {
            if (e == null || s.IsDead) return;
            s.Hunger = Clamp(s.Hunger + e.Hunger, 0f, 100f);
            s.Thirst = Clamp(s.Thirst + e.Thirst, 0f, 100f);
            s.Health = Clamp(s.Health + e.Health, 0f, s.MaxHealth);
            s.Stamina = Clamp(s.Stamina + e.Stamina, 0f, s.MaxStamina);
        }

        /// <summary>Applies damage; returns true if this damage killed the character.</summary>
        public static bool Damage(SurvivalState s, float amount)
        {
            if (s.IsDead || amount <= 0f) return false;
            s.Health = Math.Max(0f, s.Health - amount);
            return s.IsDead;
        }

        /// <summary>Speed multiplier from carried weight: 1 below the comfort limit, down to 0.55 at the hard cap.</summary>
        public static float EncumbranceSpeedFactor(float carried, float comfortLimit, float hardLimit)
        {
            if (carried <= comfortLimit || hardLimit <= comfortLimit) return 1f;
            float t = Clamp((carried - comfortLimit) / (hardLimit - comfortLimit), 0f, 1f);
            return 1f - 0.45f * t;
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
