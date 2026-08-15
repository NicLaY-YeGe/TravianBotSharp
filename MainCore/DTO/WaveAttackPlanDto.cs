namespace MainCore.DTO
{
    // A single planned "wave attack" as configured by the user in the Wave Attack tab: one
    // heavy opening wave (meant to clear defenders and/or start knocking down the wall,
    // optionally with the hero along) followed by WaveCount identical smaller waves, each
    // landing GapSeconds after the previous one's ARRIVAL (not send - see WaveAttackPlanner for
    // why send times differ). Unlike SyncAttack (multiple source villages, one send each), this
    // is a single source village sending a sequence of movements one after another.
    public sealed record WaveAttackPlan(
        VillageId VillageId,
        int TargetX,
        int TargetY,
        RallyPointEventTypeEnums EventType,
        IReadOnlyDictionary<int, long> MainWaveTroopAmounts,
        bool MainWaveIncludeHero,
        IReadOnlyDictionary<int, long> RepeatWaveTroopAmounts,
        int WaveCount,
        int GapSeconds);
}
