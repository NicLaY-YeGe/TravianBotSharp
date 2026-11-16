# PROJECT_CONTEXT.md — TravianBotSharp (Playwright branch)

Architecture and feature reference. Rules and gotchas live in `CLAUDE.md`; open work in `TODO.md`; history in
`CHANGELOG.md`. Last updated: 2026-11-15 (sections 6-7 carry later dated notes; see CHANGELOG for the full history).

## 1. Status in one paragraph

The bot was migrated from Selenium to Playwright (2026-10-18/19, full replacement behind `IChromeBrowser`).
On 2026-10-21 the Playwright build started opening the browser and running for the user ("Oldu"). Live logs
show login, adventure, daily quest, training, trapper, raid report, oasis scout and the defensive chain
(Dodge / Defend / Recall, alliance donation) all running; Dodge + Recall were verified live 2026-10-22..24 after the
positional-XPath fix in `SendTroopsCommand`. 2026-11-04/05: live logs showed an account pause caused by Start adventure (hero home Rally Point destroyed), a 30 s SendResource XPath timeout and a village lost to an attack; fixes written 2026-11-05 (not built, not live). Current focus: queue-aware scheduling, per-troop training, build-plan safety
(2026-10-25..28) and browser recovery. What is still unproven live is listed in TODO.md ("Live-verification backlog").

## 2. Solution layout

| Path | Purpose |
|---|---|
| `MainCore/Services` | `ChromeBrowser` (Playwright), `ChromeManager`, `TaskManager`, `TimerManager` (queue runner), `RxQueue` (events + account bootstrap), `CustomServiceScopeFactory` (task dispatcher), `SettingService`, `TelegramNotifier`, `LogService/LogSink` |
| `MainCore/Services/Browser` | `PlaywrightAdapters.cs` (`PlaywrightDriver`, `PlaywrightElement`, `SyncBridge`, `PlaywrightHost`), `BrowserTypes.cs` (`By`, `IWebElement`, `IWebDriver`, `BrowserException`) |
| `MainCore/Tasks` | ~38 task classes (one `[Handler]` per task); `Tasks/Base` = `BaseTask`, `AccountTask`, `VillageTask` |
| `MainCore/Commands` | `Features/*` (feature commands), `Navigate/*`, `Update/*`, `NextExecute/*`, `Misc/*`, `UI/*` |
| `MainCore/Parsers` | HtmlAgilityPack parsers (pure, unit-testable); `HtmlNodeXPathExtensions.StableXPath()` |
| `MainCore/Behaviors` | pipeline: `TaskNameLogging` → `AccountDataLogging` → `AccountTask` → `VillageTask` → `ErrorLogging` / `CommandLogging` |
| `MainCore/Entities`, `Infrasturecture/Persistence/AppDbContext.cs` | EF Core SQLite model |
| `MainCore/Enums` | `AccountSettingEnums`, `VillageSettingEnums` (settings are enum-keyed rows with defaults) |
| `MainCore/UI` | ReactiveUI view-models and input models |
| `WPFUI` | XAML views, styles, converters |
| `MainCore.Test` | xUnit tests (parsers with saved HTML fixtures, entities, behaviors, architecture rules) |
| `tools/pure-tests` | sandbox harness for parser/pure-logic tests without nuget |
| `.github/workflows` | `build.yml` (self-contained win-x64 exe artifact), `test.yml`, `patch.yml`, `release.yml` |

## 3. Runtime model

1. `RxQueue` receives `AccountInit` → `AccountInitHandler` queues `LoginTask` first, `SleepTask`, and the
   conditional bootstrap tasks (StartAdventure, HeroRevive, RaidReport, ClaimDailyQuestReward, per-village
   Update/Train/Trap/OasisScout/Upgrade …).
2. `TimerManager` runs ONE task at a time per account from a list sorted by `ExecuteAt`. Before each task it
   checks the browser is open (relaunches if not), online hours (`BypassOnlineHours` exempts
   wake-window tasks) and attack reservations (`AttackReservationRules.SelectIndex`).
3. Dispatch: `CustomServiceScopeFactory` switch → the task's generated handler, wrapped by the behaviors
   above and a Polly retry for `Retry` results.
4. Periodic tasks reschedule themselves (`Schedule next run at …` in the log).

## 4. Browser layer (Playwright)

- One persistent Chromium context per account; extensions (All-Fingerprint-Defender, Spoof-Timezone,
  WebRTC-Leak-Shield) loaded via `--load-extension`; proxy auth is native.
- `GetElement(By)` / `GetElement(Func<HtmlDocument,HtmlNode?>)` / `Wait(predicate)` poll every 250 ms up to
  180 s and fail with `Retry.Error("Timed out after 180 seconds", <expression text>)`. The expression text in
  the log (e.g. `By.XPath(xNode.XPath)`) names the exact call site that timed out.
- `browser.Html` re-reads the live DOM (`Page.ContentAsync`) on every access.
- `By.XPath(node.StableXPath())` is the supported way to turn a parsed node into a live locator; raw
  `node.XPath` is a bug (CLAUDE.md §2.2).

## 5. Feature reference (what exists)

Economy / base: UpgradeBuilding (build templates), UpdateVillage/UpdateBuilding, TrainTroop (random Min–Max) or, as an alternative mode, ShareTrain (weights, section 7),
Trapper auto-build (random Min–Max, respects level cap), SmithyUpgrade, Demolish, Celebration, ClaimQuest +
ClaimDailyQuestReward (every 20–40 min checks the "!" indicator), NPC exchange, BalanceResource,
OverflowToHammer/SupplyFromHammer, SendCrop, AutoSettle (settlers, resource pull, auto-found; backup-coordinate
list NOT implemented), CompleteImmediately.

Hero: StartAdventure (90 s budget, never Retry/Stop; a disabled Explore button is a Skip with a 45-60 min defer), HeroRevive (3-tier fallback) + SupplyForRevive.

Offence: Sync Attack tab (SyncAttackPlanTask, WaveAttackPlanTask, SendTroopsAtTimeTask incl. night-raid wake
window), Raid List (custom scheduler, per-row random resend, loot-percent troop scaling, account-wide min gap),
RaidReport (reads raid + scout reports; scout-report garrison cache `ScoutedTargetGarrison`), Combat-decision
engine Stage A (`CombatPower`, `CombatDecisionRules`, opt-in gate on Raid List; Stage B for real attacks not
wired), ScoutAutoAttackTask (auto-send after scout report, fixed source village, typed troop counts),
Buy crop (see section 7), Oasis Scout (nearest-first; hero alone if animals weak, else troop slot amount), Crop Oasis scan (one-shot,
CSV), Map Analysis tab (nearest villages from the server's `map.sql`; "Export CSV" since 2026-10-31 writes `Exports/MapScan_*.csv`, coordinate as one `x|y` column, via `MapScanCsv`).

Defence: see §6.

Overview: "Overview" tab with the state of all villages of the selected account at a glance (section 7, 2026-11-11), "Read from game" = `RefreshVillageOverviewTask`.

Session: LoginTask, SleepTask (browser closed during sleep), WakeUpTask, Telegram notifier.

## 6. Defensive subsystem (Dodge / Defend / Recall / wake window / reservation)

Trigger: `UpdateStorageCommand` and `UpdateVillageListCommand` set `village.IsUnderAttack` and queue
`DodgeTroopTask` and `DefendDonateTask` for that village when `CanStart` passes (enabled + under attack; Defend
also requires "not already handled").

**DodgeTroopTask** (village setting `DodgeEnable`)
1. `ToRallyPointOverviewCommand` → reads the real impact time via `RallyPointOverviewParser.GetIncomingMovements` (tables `inAttack` = attack, `inRaid` = raid; same timer markup). Attacks are always dodged; raids only with `DodgeRaidEnable` (default off). If the village is flagged but nothing qualifies, `IncomingMovementDiagnostics` logs what the page held and dumps it.
2. Registers the attack reservation (`AttackWakeWindow.Reserve`): from a random 3–5 min before impact to 15 s
   after, only defensive/session tasks (Dodge, Defend, Recall, Login, WakeUp) may run; others wait.
3. If now is earlier than `DodgeSendSecondsBeforeImpact` (default 30 s) before impact → reschedules **itself**
   (`task.ExecuteAt`) and, if `EnableAttackWake`, computes a wake window and queues one shared `WakeUpTask`.
4. At send time: skips if < 15 s (`MinimumSendLeadTimeSeconds`) remain; switches directly to the Send-troops tab
   (tab index 2), reads available counts for the slots in `DodgeTroopSlotsMask`, then
   `SendTroopsCommand` (type `AttackNormal`) to the fixed `DodgeTargetX/Y` (any coordinate works).
5. Queues `RecallTroopTask` for `DodgeRecallSecondsAfterSend` (default 50 s) later, inheriting the wake window.

**RecallTroopTask** → `ToRallyPointOverviewCommand`, then `RecallTroopCommand` finds the outgoing-attack abort
button by target coordinate (`RallyPointOverviewParser.GetOutgoingAttackAbortButton`) and clicks it. Travian
allows cancelling for 90 s after sending; cancelling returns troops as if they had travelled partway back. If the
button is missing it logs a warning with the movement headlines and returns Ok (never blocks).

**DefendDonateTask** (village setting `DefendDonateEnable`) — since 2026-10-22 it trains and then DONATES again (see the 10-22 afternoon CHANGELOG entry; the merchant steps below describe the 10-19..10-21 behaviour and are no longer in the task)
Reacts to attacks AND raids (since 2026-10-23). Two passes per movement since 2026-10-24: pass 1 at detection, pass 2 (same task instance, rescheduled) 15 min before impact. (`DefendAttackMemory`, handled until impact + 90 s). Under a hard time budget
(`attackSeconds − (dodgeLead + 15 s)` when Dodge is on, `− 15 s` otherwise; skipped if < 25 s):
1. `TrainCheapestTroopCommand`: cheapest troop at the Barracks with the whole stock (stock read from DB `Storage`).
2. Re-reads the remaining stock off the page, opens Marketplace; if no free merchants → logs and ends.
3. Scales the plan to merchant capacity (`DefendResourceTargetRules.ScaleToCapacity`), logs
   `Defend send plan …`, picks a random other village (prefers room in storage, avoids villages under attack,
   `ChooseTarget`) and `SendResourceCommand`.
Every failure is `Skip` (never Retry/Stop); a user pause surfaces as `Cancel`.

**Wake window**: `AttackWakeWindow.Apply` + `WakeWindowRules.ComputeAttackWindow` (settings
`EnableAttackWake`, default ON; `AttackWakeBefore/After Min/Max Minutes`). Limitation by design: the attack must be
detected while the bot is awake (no sentinel check-ins).

### 6.1 Live-log verification — 2026-10-21 17:47–17:50 (village 55031 in TS-3202)

| Feature | Evidence in log | Verdict |
|---|---|---|
| Attack detection + reservation | 17:47:31 "Attack on 55031 lands 17:49:26: other tasks wait from 17:45:22 until 17:49:41" | Worked |
| Wake window | 17:47:31 "Wake window … 17:41:11 → 17:58:55 (first action 17:48:56, impact 17:49:26)" | Worked (bot already awake, no WakeUpTask needed) |
| Dodge scheduling | 17:47:31 "lands in 115s – will send dodge troops in 85s"; task re-ran exactly at 17:48:56 | Worked |
| Defend – training | 17:47:38 budget 63 s; 17:47:41 "Trained 95x Phalanx in 55031 before incoming attack" | Worked (3 s) |
| Defend – send resources | 17:47:46 "No free merchants in 55031, skipping the resource send (trained 95)" | Skipped by design; not proven that merchants were really 0 (see TODO P1) |
| Dodge – send | 17:49:00 `SendTroopsCommand` AttackNormal → (35|-5) started; no "Sent troops…" line, no "Dodged … will recall" line; next line 17:50:54 "Pause button is pressed" (114 s later) | **Did NOT complete.** Impact was 17:49:26 — the troops were not confirmed out |
| Recall | never queued (queued only after a successful dodge send) | **Not exercised / unverified** |

Same hang pattern in the oasis scout earlier that day: sends to (39|-4) ×2 and (38|-7) hung (two ended in
`Timed out after 180 seconds` with expression `By.XPath(xNode.XPath)` = the X-coordinate input), while sends
to (37|0) ×2 and (42|-5) (hero) succeeded in 2–3 s. `SendTroopsCommand` is the only common code and it is the
only place that still looked elements up with raw `node.XPath` (6 call sites). Not proven by a DOM capture; since 2026-10-22 a stalled send saves page HTML + screenshot to `Screenshots/`.

### 6.2 Live-log verification — 2026-10-22 (log 00:01–11:17, village 55031 in TS-3202)

| Event | 07:45 attack (old build) | 11:06 attack (build with the SendTroops fix, `TimeoutSeconds=23`) |
|---|---|---|
| Detection, reservation, wake window, WakeUpTask | worked | worked |
| Defend, training | 1 Phalanx (clay/iron-limited) | 3 Phalanx |
| Defend, send resources | "No free merchants in 55031" | same |
| Dodge send | **failed**: `Typing into the element timed out: Timeout 30000ms exceeded` at 08:00:42 (impact 08:00:36); retry found no attack | **worked**: sent 11:14:38 (2 s), impact 11:15:01 |
| Recall | never queued | **worked**: 11:15:37 (59 s after send, inside the 90 s cancel window) |

Send failures before the fix (00:01–10:54): 59 "Typing…timed out" + 47 "180 s" (38× `By.XPath(node.XPath)`, 7× `xNode`,
2× `yNode`) vs 22 successful sends. After the fix (from ~10:54): 0 timeouts, 2 of 2 sends OK (small sample).
Merchants: 55031 showed "No free merchants" for 3 hours (last own send 08:02:59) at the 11:06 defend and in
Balance/Overflow tasks — either real or a parser false-negative; undecided, which is why Defend now donates instead.

## 7. Settings (selected keys)

**Village setting tab layout (2026-11-16).** Sections + collapsed `Expander` per feature, `● ON` badge in the header, a feature's detail fields hidden while its own switch is off (XAML `Visibility` binding, values untouched), long explanations in tooltips. All `x:Name`s are unchanged because `VillageSettingTab.xaml.cs` binds by name: a new setting needs its field added to the XAML inside the right section AND a binding line there. Not built, not live.

**Receiver room, idle reload, planned waits (2026-11-15).** A shipment of Balance resources / Overflow to hammer fills the receiver only up to just BELOW the percent at which the receiver would send resources away itself (`ReceiveRoomRules`: Auto balance overflow % and, for a non-hammer village, Overflow to hammer %); no village with such room = no Balance task. Overflow to hammer is queued only when the hammer can take one merchant load. `AccountTaskBehavior` reloads the page before judging it when the account's previous task ended >= 30 min ago (`IdleSessionRules`, `AccountActivity`). `NextExecuteError` results (queue full, prerequisite in queue) are logged as Information, not Warning. Not built, not live.

**Crash hardening (2026-11-14).** `CrashLog` (process-wide unhandled/unobserved exception handlers + UI-thread handler, file `Logs/crash.log`), try/catch/finally around `TimerManager`'s `Elapsed` handler (exception = log + pause the account + keep the loop), a dead Playwright driver (`Process exited` on launch) is replaced once inside `ChromeBrowser.Setup`, reopen attempts back off (5 s per failure beyond the 3rd, 60 s max) with a Telegram message after 10 and 100 failures, `LogSink` locked. Not built, not live.

**Share-based troop training (2026-11-13).** A MODE of its own (village setting `TrainShareEnable`, default off) next to the old per-troop/horizon training; the two exclude each other through `CanStart` and `SaveVillageSettingCommand` (old settings are kept, switching back needs no re-entry). `ShareTrainTask` (village task, secondary: 300 s budget, never Retry/Stop, ends with ExecuteAt unchanged) is queued by `UpdateStorageCommand` when the stock above the construction reserve floor is worth at least `TrainShareTriggerPercent` of the storage (3 x Warehouse + Granary) and the memory-only "not before" (`ShareTrainMemory`: 3-5 min after a trained visit, 10-20 min otherwise) has passed. There are no visit times and no unit amounts: each of the 10 slots (Barracks 1-4, Stable 1-4, Workshop 1-2, tribe relative) has a weight (0 = not trained); `ShareTrainRules.Allocate` splits the surplus per resource by weight, lets what a troop cannot use flow on to the others (waterfall) and respects a per-building queue ceiling `TrainShareQueueMaxMinutes` (queue already running counts). A visit = survey pass (cost, unit time, queue, trainable per building) + training pass through `TrainTroopCommand` with an explicit amount. Needs `ConstructionReservePercent` > 0 (validator + run-time check). Only own stock above the floor: no NPC, no hero inventory, no ConstructionFirst, no training kick, no Great Barracks/Stable. Not built, not live.

**Village overview tab (2026-11-11).** Tab "Overview" (`VillageOverviewViewModel`): one row per village of the selected account - resources (fill %, crop balance), Smithy and Celebration (from the database / village settings, always current), troops at home / reinforcements of own villages / outside, merchants home / away, Barracks / Stable / Workshop queue ends (Great Barracks / Stable not included). Troops, merchants and queue ends are MEMORY-ONLY snapshots in `VillageOverviewStore` (no database table: no migrations, see AppDbContext "schema patches"). Writers: `RefreshVillageOverviewTask` ("Read from game", one-shot account task, 100 s per village, never Retry/Stop; Rally Point overview via `RallyPointTroopsParser` incl. the "Show all" links - the page lists only 10 movements per section - Marketplace via `SendResourceParser.GetMerchantCounts`, training pages via `TrainQueueParser`), plus passive writes from `ToSendResourcePageCommand` (merchants) and `TrainTroopTask` (queue end). Cell texts: `VillageOverviewRules` (pure). Construction column (2026-11-12): running jobs from `QueueBuildings`, build-list size from `Jobs` (`idle, N job(s) waiting`), always current from the database. Not built, not live.

**Raid list troop shortage (2026-11-11).** A shortage of troops/hero defers only the row that found it (8-14 min, `RaidListShortageRules`); the whole list is paused (+ Telegram) only after 6 shortages in a row for the same row. A successful send resets the streak. Memory-only streak.

**Buy crop (2026-11-10).** `BuyCropTask` (village task, secondary: 120 s budget, never Retry/Stop) buys crop on the Marketplace Buy tab when crop + crop still travelling is below `BuyCropGranaryPercent` of the Granary, up to `BuyCropTargetPercent`, independent of the NPC exchange and ignoring the construction reserve. `BuyCropRules` picks the offer (crop, 1:1 or better, Accept enabled, enough free merchants, trip <= `BuyCropMaxMinutes`, no overflow; shortest trip first, only offers that arrive before `Storage.FreeCrop` runs the store dry while one exists, then the fullest paying store); up to 3 offers per run, each re-checked against the live row right before the click and confirmed by the merchant count / row count. `BuyCropMemory` (memory only) holds the crop in transit and the "not before" time. Queued from `UpdateStorageCommand` / `SaveVillageSettingCommand`, not bootstrapped in RxQueue. Parser: `MarketplaceBuyParser` (Buy tab found by `href` ending `t=1`; Accept disabled = CSS class). Not built, not live.

**Catapult target (2026-11-08).** `SendTroopsCommand.Command.CatapultTarget` (`CatapultTargetEnums`, 0 = not set) is chosen on the confirmation screen before the final click, only when the page offers it (Warning otherwise, never a failure). Sync Attack and Wave Attack pass one plan-wide target (combo next to the movement type) through `SendTroopsAtTimeTask`; the probe never sets it; Raid List is excluded (raids fire no catapults). One target only.

**Training kick (2026-11-07).** After `UpgradeBuildingTask` starts a building it moves the village's `TrainTroopTask` to now + 10-20 s (`TrainKickRules`, only earlier, only when training is enabled); intervals, queue state, horizon and the reserve still decide what is trained.

**Construction reserve (2026-11-07).** Village setting `ConstructionReservePercent` (0 = off). `ConstructionReserveRules.Floor` = max(percent of capacity, cost of the next building) per resource, capped at the capacity (Warehouse for wood/clay/iron, Granary for crop); `TrainTroopTask` spends only the stock above it. The cost comes from `HandleResourceCommand` and the "build queue full until" note from `UpgradeBuildingTask`, both kept in memory in `ConstructionNeed` (cost expires after 6 h). While the build queue still runs > 30 min only the percent part holds. With a floor NPC trades for training are skipped; hero inventory still adds on top. A floor, not a production split: other stock consumers are not limited.

**Smithy / new villages (2026-11-07).** `SmithyUpgradeTask` is a secondary task: 90 s budget, every failure becomes a 10-20 min busy-until (`SmithyUpgradeBusyUntilUnixTime`), never `Stop`; the research block gets 8 s to appear before it counts as missing. `ApplyBuildTemplateTask` queues `UpgradeBuildingTask` after writing the template jobs. New villages get `UseHeroResourceForBuilding` = 1 (existing rows unchanged).

**Plan vs slot (2026-10-28).** A build job is valid only if its slot is empty, holds the plan's own building, or has that building under construction (`BuildPlanSlotRules`).
`GetBuildPlanCommand` deletes any other job with a Warning; `HandleUpgradeCommand` refuses to click Upgrade on a different building. Oasis scout skips the animal oases of a run once the hero is away.

**Browser recovery (2026-10-28).** `AccountTaskBehavior` closes the browser (-> `BrowserClosed` -> re-open + re-login) only for a `BrowserException`
that is not transient (`BrowserErrorRules.IsTransient`). `PlaywrightDriver.PageSource` repeats a read that failed because a navigation was in flight
(6 x 500 ms); a navigation that outlasts that makes the task Skip and come back 10 s later with the browser still open.

**Oasis scout, animal oases (2026-11-02).** Hero first (at home, animals within `OasisScoutHeroPowerThreshold`); otherwise, when `OasisScoutAnimalTroopSlot` > 0, a random `OasisScoutAnimalMin/MaxTroops` of that slot, unless the estimated animal power exceeds `OasisScoutAnimalMaxPower` (0 = no limit). Empty oases keep using `OasisScoutTroopSlot`.

**Per-troop intervals in the horizon flow (2026-11-02).** `TrainWithHorizon` skips a troop whose own "not again before" time (`TrainTroopSchedule`, key village/building/troop) has not come, sets it after training (only when the troop has its own interval; 0/0 = no limit there), and `QueueHorizonRules.HoldUntilAnyTroopDue` keeps the building visit from coming before the earliest troop is due.

**Visit planning for starved villages (2026-11-02).** `QueueHorizonRules.PlanNextVisit` decides the next visit of a horizon building: a village that could not pay (page max below the wanted units, or `MissingResource`) is not visited before the 5-10 min starved retry and never earlier than the regular "queue reaches at least" time; a miss caused by the per-visit cap keeps the old "earlier of regular and retry"; the pre-offline lead point always wins. `SupplyBackoff` (memory only) stops a source that just reported "nothing spare" from being asked again for the same target for 8-12 min (`SupplyFromHammerTask.RequestIfNeeded`).

**Training queue horizon (2026-10-30).** Per building (Barracks, Stable, Workshop, Great Barracks/Stable) the village settings `<Building>HorizonMin` ("at least", minutes) and `<Building>HorizonTarget` ("fill up to") keep the queue filled AHEAD of time; 0/0 = off = the queue-end behaviour of 2026-10-25/26. `TrainTroopTask` hands such a building to `TrainWithHorizon`: queue seconds from `TrainQueueParser`, target from `QueueHorizonRules.FillToSeconds` (offline-aware through `OnlineHoursRules` and the account setting `TrainOfflineCoverMaxMinutes`), units from the per-unit time (`TrainTroopParser.GetUnitSeconds`) capped by the troop's "amount max", next visit from `QueueHorizonRules.NextVisit`. A shortfall is made up by `MakeUpResources`: own stock -> NPC trade (`TrainUseNpc`) -> hero inventory above `HeroResourceReserve` (`TrainUseHeroResource`); the rest is trained partially. Sleep is not considered.

**Hero resources (2026-10-29).** `UseHeroResourceCommand` takes the missing amount rounded up to 100 via `HeroResourceAmountRules` (multiples stay, never above the hero's stock while the stock covers the real need, then clamped to storage capacity). `HeroReviveTask` keeps its own correct copy of the rounding.
**Transient browser errors (2026-10-29).** `TransientStreak` counts consecutive "page is navigating" failures per account; after `BrowserErrorRules.MaxConsecutiveTransient` (6, about a minute) `AccountTaskBehavior` closes the browser as before.

**Merchant loads / queue timers (2026-10-25).** Merchant sends are multiples of 750 (`MerchantAmountRules`: surplus sends floor, need sends ceil within spare). Training-queue timers are cumulative: queue end = last `td.dur` timer in `table.under_progress` (real Barracks capture); `tr.next` is ignored.

**Troop training (2026-10-26).** Each training building has its own "not again before" interval (`<Building>RepeatTimeMin/Max`, minutes; 0/0 = general
`TrainTroopRepeatTimeMin/Max`); `TrainTroopTask` keeps a per-building not-before (`TrainTroopSchedule`) and wakes for the earliest; after a batch the next
visit is max(queue end + buffer, interval). `ConstructionFirst` (village setting, default off): while `UpgradeBuildingTask` has a plan waiting for resources
(`ConstructionHold`, max 3 h) that village trains nothing.

**Per-troop training (2026-10-27).** Barracks/Stable/Workshop settings are per troop: slot N (tribe-relative, `TrainTroopSlots`) has amount min-max and interval min-max (minutes; 0/0 = general fallback `TrainTroopRepeatTime`, hidden in the UI). `TrainTroopPlanner` decides the plans (old single-troop keys remain a fallback; Great Barracks/Stable still use them). `TrainTroopSchedule` is keyed by (village, building, troop). **Crop-blocked plans (2026-10-27):** `UpgradeBuildingTask` queues a cropland upgrade first (`AddCroplandCommand` returns false when none is left -> building tasks parked 3 h, Warning log).

**2026-10-25 additions.** `EnableAttackCheck` (account, default ON): `AttackCheckTask` reloads dorf1 every 4-6 min (random,
`AttackCheckRules`); the existing `AccountTaskBehavior` -> `UpdateVillageListCommand` reads all villages' attack flags from
the village list and starts Dodge/Defend for a newly flagged village. `HeroHealthGate` + `HeroHealthReader`: `MinHeroHealthPercent`
now gates both StartAdventure and Oasis scout (shared reading, 10 min). `QueueDeferRules` / `QueueWait` / `TrainQueueParser`:
Smithy, Small celebration, Trapper and troop training wait for the game-side queue end + 30-60 s (details in CHANGELOG
2026-10-25). Rule of thumb: a task that wants to run again MUST change `ExecuteAt` (TimerManager removes a task whose
ExecuteAt is unchanged, also after Skip).

Account: `EnableAttackWake`, `AttackWakeBefore/AfterMin/MaxMinutes`, `WorkTimeMin/Max`, `EnableAutoHeroRevive`,
`MinHeroHealthPercent`, combat-check and scout-auto-attack keys.
Village: `DodgeEnable`, `DodgeTroopSlotsMask` (bit slot−1, slots 1–10), `DodgeTargetX/Y`,
`DodgeSendSecondsBeforeImpact`, `DodgeRecallSecondsAfterSend`, `DefendDonateEnable`, `EnableOasisScout`,
`OasisScoutMaxDistance/Interval/TroopSlot/Min/MaxTroops/HeroPowerThreshold`. Adding an enum value requires
updating the enum-count assertions in `MainCore.Test`.

## 8. Test & delivery workflow

Claude edits → static checks (`tools/pure-tests/run.sh`, brace balance) → zip of changed files (+ explicit list of
deleted files) → user copies into the repo with GitHub Desktop (branch `PlaywrghtYGG`, PR #1) → GitHub Actions
builds → user downloads artifact and live-tests → pastes log. Nothing is "confirmed" until that last step.

## 9. Known gaps (summary; details in TODO.md)

3-minute waits on time-critical paths (Dodge/Defend have their own limits, others do not); AutoSettle backup coordinates; Raid List bulk-add dedupe
and row editing; combat-decision Stage B; unscoped village lookups; dead `SendReinforcementCommand.cs`.
