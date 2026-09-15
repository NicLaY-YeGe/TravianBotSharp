namespace MainCore.Errors
{
    // Distinguishes "hero is dead" from a genuine navigation/timeout failure. Without this,
    // code that needs the hero's Inventory tab (e.g. ToHeroInventoryCommand) would otherwise
    // sit waiting on a page state that can never occur while the hero is dead - clicking the
    // hero avatar lands on the Attributes/revive screen instead - until the WebDriver wait
    // itself times out (see CLAUDE.md 2026-09-12 note).
    public class HeroDead : Error
    {
        private HeroDead() : base("Hero is dead")
        {
        }

        public static Result Error => new HeroDead();
    }
}
