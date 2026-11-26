namespace MainCore.Commands.Features.SendResource
{
    // 2026-11-26 (live log 2026-11-26 04:26-04:38, village 33411): Defend trained 63 Phalanx and donated 234k to the
    // alliance, which emptied the village. 27 s later Balance resources picked it as the receiver with "the most free
    // room" and shipped 39.7k crop (04:27) and 11k iron + 30.7k crop (04:31) into it, 7-11 min before the attack landed.
    // The shipments were plundered. Only Defend's own target choice looked at the attack flag.
    //
    // Rule (CLAUDE.md 27): a task that ships resources TO a village must not pick one whose IsUnderAttack flag is set
    // (attack or raid, the flag covers both). The flag is the one the bot already keeps (UpdateStorageCommand /
    // UpdateVillageListCommand / Attack check). A stale flag only delays a shipment, a missing check loses it.
    // AttackedReceiverGuardTest makes sure every file that sends resources to another village refers to this class.
    public static class AttackedReceiverGuard
    {
        public static bool IsUnderAttack(AppDbContext context, VillageId villageId)
            => context.Villages.Any(x => x.Id == villageId.Value && x.IsUnderAttack);
    }
}
