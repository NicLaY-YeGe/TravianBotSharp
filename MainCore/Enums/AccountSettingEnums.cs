namespace MainCore.Enums
{
    public enum AccountSettingEnums
    {
        ClickDelayMin = 1,
        ClickDelayMax,
        TaskDelayMin,
        TaskDelayMax,
        EnableAutoLoadVillageBuilding,
        UseStartAllButton,
        FarmIntervalMin,
        FarmIntervalMax,
        Tribe,
        WorkTimeMin,
        WorkTimeMax,
        SleepTimeMin,
        SleepTimeMax,
        HeadlessChrome,
        EnableAutoStartAdventure,

        // The village that other villages can request resource top-ups from, for building
        // upgrades they're short on. 0 = none configured, otherwise a Village.Id.
        HammerVillageId,

        // Never let the hammer village's own stock drop below this % of capacity when
        // sending resources away, so troop training doesn't stall.
        HammerReservePercent,

        // Bitmask of which hours of the day (0-23, bit N = hour N) the account is allowed
        // to run tasks. Default = all 24 bits set (no restriction). Browser stays open
        // outside allowed hours, the bot just won't start a new task until an allowed
        // hour comes around.
        OnlineHoursMask,

        // Don't send the hero on an adventure if their health is below this percent
        // (0-100). Default = 0, meaning no restriction (always send when an adventure
        // is available). Checked live on the hero/attributes page before departure.
        MinHeroHealthPercent,

        // 2026-09-12: opt-in master switch for HeroReviveTask's 3-tier auto-revive (own bag ->
        // sibling villages -> NPC trade with gold). Default OFF (opt-in), unlike
        // VillageSettingEnums.AutoApplyBuildTemplateEnable - this is a brand-new automation,
        // not a replacement for prior always-on behavior, so there's no "must default true to
        // avoid a regression" concern here.
        EnableAutoHeroRevive,

        // Account-wide "earliest time the NEXT raid list send (from ANY row) is allowed to
        // fire" gate, stored as whole minutes since the Unix epoch (fits comfortably in an
        // int - see RaidListTask.ToEpochMinutes/FromEpochMinutes). 0 = no gate set yet, i.e.
        // unrestricted (the very first raid list send for an account fires immediately).
        //
        // 2026-09-16, user request: RaidListEntry rows were each scheduled on their own
        // fully independent random(min,max) clock - fine for a couple of rows, but with a
        // large bulk-added list (e.g. 50 targets) the independent random offsets cluster
        // tightly by chance (50 points spread over a 60-minute window average ~1.2 minutes
        // apart), so raids kept firing back-to-back in practice, crowding out other queued
        // tasks and looking like a ban-risk burst pattern. RaidListTask now reads/advances
        // this single shared value so every send in the list - regardless of which row -
        // is separated from the previous one by that row's own random(IntervalMinMinutes,
        // IntervalMaxMinutes), turning the whole list into one serialized chain instead of
        // many independent clocks that happen to overlap.
        RaidListNextAllowedSendAtMinutes,

        // 2026-09-19, user request ("Yagma organize", scope A): master switch for
        // RaidReportTask, which reads /report/offensive, matches raid reports to Raid List rows
        // and pauses a row that keeps losing troops (see RaidReportRules). Default ON - unlike
        // EnableAutoHeroRevive it never sends anything or spends anything, and with no active
        // Raid List row the task does not even open the browser page.
        EnableRaidReport,

        // Internal (no UI, not part of AccountSettingInput): the highest offensive report id
        // RaidReportTask has already handled. Report ids grow with time, so "id > this" is a
        // reliable "new report" test - unlike the read/unread flag, which flips when the user
        // reads a report by hand. 0 = never run yet (the first run only records a baseline and
        // never pauses anything based on old history).
        RaidReportLastId,

        // 2026-09-20, user request: gap between two CONSECUTIVE raid sends (any rows), in
        // seconds, picked at random in [Min, Max] after every successful send. Before this the
        // gap re-used the sent row's own repeat interval (IntervalMin/MaxMinutes, e.g. 30-60
        // min), so a 50-row list took 25-50 hours per round and troops sat idle in the village.
        // The row's own interval still decides when THAT row raids again; this only spaces the
        // chain. Defaults 30-90 s (human-like clicking speed).
        RaidListSendGapMinSeconds,
        RaidListSendGapMaxSeconds,

        // Internal (no UI): earliest time the next raid send is allowed, in seconds since
        // 2026-01-01 00:00 UTC (fits int32 for ~68 years). Replaces
        // RaidListNextAllowedSendAtMinutes, which is no longer read or written (minute
        // resolution is too coarse for a seconds-range gap). 0 = no gate.
        RaidListNextAllowedSendAtSeconds,

        // 2026-09-27, user request ("raporlara göre optimize yağma otomasyonu", combat-decision
        // idea unshelved from 2026-09-21): master switch for CombatDecisionRules being consulted
        // before a Raid List send - see RaidListTask. Default OFF, unlike EnableRaidReport: this
        // one CAN change what gets sent (a skip), so it starts opt-in.
        EnableCombatCheckOnRaidList,

        // Same idea, for real Attack sends (SyncAttack/WaveAttack) - staged separately (not yet
        // wired as of 2026-09-27, see TODO.md) because the same "skip and reschedule" shape
        // RaidListTask uses doesn't map onto Sync/WaveAttackPlanTask's one-shot plans as cleanly.
        // Default OFF.
        EnableCombatCheckOnAttack,

        // How old a ScoutedTargetGarrison row is allowed to be and still be trusted, in hours.
        // Older than this (or no row at all for the target) = CombatDecisionRules is not
        // consulted at all and the send behaves exactly as before this feature existed (2026-09-
        // 27 user decision - explicit fallback, not "assume undefended"). Default 12h - long
        // enough that a normal raid-list cadence usually still has a recent-enough scout, short
        // enough that the target's garrison hasn't fully regrown since.
        CombatCheckMaxAgeHours,

        // Internal (no UI): the highest scouting report id RaidReportTask's scout-report pass
        // has already handled (see ProcessScoutReports) - same "id > this" freshness test as
        // RaidReportLastId, and same first-run behaviour (baseline only, nothing evaluated).
        ScoutReportLastId,

        // How much our (rough) attack points must exceed the target's (rough) blended defense
        // points before sending, as a percent margin - e.g. 20 means we need our attack points
        // to reach 120% of their defense points before sending; exact break-even (100 vs 100)
        // skips. See CombatDecisionRules.Decide. Default 20.
        CombatSafetyMarginPercent,

        // 2026-10-03, user request: when an incoming attack was detected while awake but lands
        // during sleep / an offline hour, wake the account BEFORE the first defensive action
        // (Dodge / Defend+Donate) and go back to sleep after the impact - see
        // WakeWindowRules.ComputeAttackWindow and AttackWakeWindow. Default ON (user decision).
        // The four values are minutes, a random pick in [Min, Max] each side.
        // APPENDED at the end on purpose: enum values are positional in existing databases.
        EnableAttackWake,
        AttackWakeBeforeMinMinutes,
        AttackWakeBeforeMaxMinutes,
        AttackWakeAfterMinMinutes,
        AttackWakeAfterMaxMinutes,
    }
}