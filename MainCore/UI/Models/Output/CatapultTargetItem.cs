using System.Text.RegularExpressions;

namespace MainCore.UI.Models.Output
{
    // One entry of the "Catapult target" ComboBox (Raid List tab). NotSet = leave the game's own
    // default (Random). Random itself is not offered - it is what "not set" already gives.
    public sealed record CatapultTargetItem(CatapultTargetEnums Target, string Name)
    {
        public override string ToString() => Name;

        public static List<CatapultTargetItem> All()
        {
            var items = new List<CatapultTargetItem> { new(CatapultTargetEnums.NotSet, "Not set (game default)") };
            items.AddRange(Enum.GetValues<CatapultTargetEnums>()
                .Where(x => x != CatapultTargetEnums.NotSet && x != CatapultTargetEnums.Random)
                .OrderBy(x => (int)x)
                .Select(x => new CatapultTargetItem(x, Regex.Replace(x.ToString(), "(?<=[a-z])(?=[A-Z])", " "))));
            return items;
        }

        public static CatapultTargetItem For(IEnumerable<CatapultTargetItem> items, int value)
        {
            return items.FirstOrDefault(x => (int)x.Target == value) ?? items.First();
        }
    }
}
