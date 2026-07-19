using Humanizer;

namespace MainCore.Models
{
    public class ResourceBuildPlan
    {
        public int Level { get; set; }
        public ResourcePlanEnums Plan { get; set; }

        // false (default) = keep upgrading whichever field has the lowest level (roughly the
        //                    lowest hourly production), like before.
        // true             = instead upgrade a field of whichever resource type currently has
        //                    the smallest amount sitting in the village's warehouse/granary.
        public bool PriorityLowestStock { get; set; }

        public override string ToString()
        {
            var priority = PriorityLowestStock ? "lowest stock" : "lowest level";
            return $"{Plan.Humanize()} to level {Level} (priority: {priority})";
        }
    }
}