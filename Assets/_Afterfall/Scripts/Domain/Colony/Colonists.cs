using System;
using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Items;
using Afterfall.Domain.Survival;

namespace Afterfall.Domain.Colony
{
    public enum JobType
    {
        Haul,
        Build,
        Eat,
        Rest,
        Farm
    }

    public enum FarmAction
    {
        Plant,
        Water,
        Harvest
    }

    public enum JobState
    {
        Open,
        Claimed,
        Completed,
        Failed
    }

    /// <summary>Priority buckets (lower runs first).</summary>
    public static class JobPriority
    {
        public const int Emergency = 0;
        public const int Survival = 1;
        public const int Safety = 2;
        public const int Productive = 3;
        public const int Optional = 4;
    }

    public enum HaulStage
    {
        ToSource,
        ToDestination
    }

    /// <summary>
    /// A unit of work on the job board. Hauls hold a reservation at the source (items) and at
    /// the destination (capacity, or a blueprint delivery quota) from claim until completion;
    /// failures release them and record why.
    /// </summary>
    public sealed class Job
    {
        public EntityId Id { get; set; }
        public JobType Type { get; set; }
        public int Priority { get; set; } = JobPriority.Productive;
        public JobState State { get; set; } = JobState.Open;
        public EntityId AssigneeId { get; set; }
        public string Description { get; set; } = "";
        public string FailureReason { get; set; } = "";
        public double CreatedAt { get; set; }

        /// <summary>Build: the blueprint. Haul: the destination structure. Rest: the bed.</summary>
        public EntityId TargetStructure { get; set; }

        // Haul data
        public EntityId SourceInventory { get; set; }
        public EntityId DestinationInventory { get; set; }
        public string ItemId { get; set; } = "";
        public int Quantity { get; set; }
        public HaulStage Stage { get; set; }
        public ReservationToken SourceToken { get; set; }
        public ReservationToken DestinationToken { get; set; }

        /// <summary>True when the destination is a blueprint (materials are delivered, not stored).</summary>
        public bool DeliverToBlueprint { get; set; }

        /// <summary>Farm jobs: what to do at the planter bed.</summary>
        public FarmAction Farm { get; set; }

        public bool IsActive => State == JobState.Open || State == JobState.Claimed;
    }

    /// <summary>Per-colonist work settings: which job types they do and how eagerly (0 = never, 1 highest … 4 lowest).</summary>
    public sealed class WorkSettings
    {
        public Dictionary<JobType, int> Priorities { get; } = new Dictionary<JobType, int>
        {
            [JobType.Haul] = 3,
            [JobType.Build] = 3,
            [JobType.Farm] = 3
        };

        public int PriorityFor(JobType t) => Priorities.TryGetValue(t, out int p) ? p : 0;
    }

    public enum ColonistActivity
    {
        Idle,
        Moving,
        Working,
        Eating,
        Resting,
        Down
    }

    public sealed class ColonistState
    {
        public EntityId Id { get; set; }
        public string Name { get; set; } = "";
        public bool Recruited { get; set; }
        public Vec3 Position { get; set; }
        public float Yaw { get; set; }
        public SurvivalState Vitals { get; set; } = new SurvivalState();
        public Inventory Carry { get; set; }
        public Dictionary<JobType, float> Skills { get; } = new Dictionary<JobType, float>();
        public WorkSettings Work { get; set; } = new WorkSettings();
        public EntityId CurrentJob { get; set; }
        public ColonistActivity Activity { get; set; }
        public string Status { get; set; } = "Waiting for work";
        public EntityId HomeBed { get; set; }

        /// <summary>Where the colonist is walking to (set by the colony system, executed by the movement adapter).</summary>
        public Vec3 MoveTarget { get; set; }

        public bool HasMoveTarget { get; set; }
        public float Morale { get; set; } = 60f;

        /// <summary>Most recent failure reasons for the strategic UI.</summary>
        public List<string> RecentFailures { get; } = new List<string>();

        public float Skill(JobType t) => Skills.TryGetValue(t, out float s) ? s : 1f;

        public void RecordFailure(string reason)
        {
            RecentFailures.Insert(0, reason);
            while (RecentFailures.Count > 5) RecentFailures.RemoveAt(RecentFailures.Count - 1);
        }
    }

    /// <summary>Moves colonists in the world. The game uses NavMesh agents; headless runs move in straight lines.</summary>
    public interface IColonistMovement
    {
        /// <summary>Advances movement toward <see cref="ColonistState.MoveTarget"/>; returns true when within <paramref name="arriveDistance"/>.</summary>
        bool Step(ColonistState c, float speed, float arriveDistance, float dt);

        /// <summary>True when there is no route to <see cref="ColonistState.MoveTarget"/> (the colonist has stalled short of it).</summary>
        bool CannotReach(ColonistState c);
    }

    public sealed class DirectColonistMovement : IColonistMovement
    {
        public Func<Vec3, float> GroundHeight { get; set; }

        public bool Step(ColonistState c, float speed, float arriveDistance, float dt)
        {
            Vec3 delta = c.MoveTarget - c.Position;
            delta.Y = 0f;
            float dist = delta.Magnitude;
            if (dist <= arriveDistance) return true;
            float step = Math.Min(dist, speed * dt);
            Vec3 next = c.Position + delta.Normalized * step;
            if (GroundHeight != null) next.Y = GroundHeight(next);
            c.Position = next;
            c.Yaw = (float)(Math.Atan2(delta.X, delta.Z) * 180.0 / Math.PI);
            return dist - step <= arriveDistance;
        }

        public bool CannotReach(ColonistState c) => false;
    }
}
