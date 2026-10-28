# CHANGELOG.md — TravianBotSharp (Playwright branch)

Newest first. "Verification" says how far a change got: static (read/checked only), built (CI green), live (user ran it).

## 2026-10-27 (evening) — Crop-blocked plan queues a cropland upgrade; per-troop training settings (code, NOT yet built or tested live)

A) Crop production negative ("Increase crop production." on any plan, e.g. Grain Mill):
- `UpgradeBuildingTask`: instead of only waiting, a cropland upgrade is queued as the FIRST job of the village (`AddCroplandCommand`, lowest-level cropland below max)
  and the loop continues, so the cropland is built next. Max 2 such additions per run. A cropland plan itself is exempt (it just waits).
- `AddCroplandCommand` now returns bool and no longer throws: false = no cropland left (all at max / none read). Then the village's building task is
  suspended for 3 h (`UpgradeWaitRules.NoCroplandSuspendHours`) with a Warning log naming the plan. The old `LackOfFreeCrop` branch got the same park-and-log fallback.
- Open: "suspended" = the Upgrade building task of that village sleeps 3 h (other tasks keep running); other jobs behind the blocked plan wait too.

B) Troop training, per troop (user request): every troop has its own amount (min-max) and its own "not again before" interval (min-max minutes).
- 40 new enums appended (`Train{Infantry1-4|Cavalry1-4|Siege1-2}{AmountMin|AmountMax|RepeatMin|RepeatMax}`), defaults 0. Slot N = Nth troop of the building for the village tribe
  (Barracks = infantry, Stable = cavalry, Workshop = siege). A slot is active when its amount is > 0.
- `TrainTroopSlots` / `TrainTroopPlanner` (pure): plans per building; Great Barracks/Great Stable keep the old single-troop settings (troop + amount + interval, still in the UI);
  a config saved before this (old BarrackTroop etc.) keeps working as a fallback and appears as that troop's row in the UI. Saving the village settings writes the old Barracks/Stable/Workshop keys as 0.
- UI: the general "next visit" row and the single-troop Barracks/Stable/Workshop rows are gone; three tables (rows depend on the tribe): troop | amount min-max | not again before min-max.
  The general interval (default 120-180 min) stays as hidden fallback for a troop whose interval is 0/0. Interval never below 60 s.
- `TrainTroopTask`: one visit trains every troop that is due (page reopened between troops); queue logic unchanged (busy queue -> queue end + 30-60 s buffer; after a batch
  max(troop interval, queue end + buffer); starved troop -> its interval, others continue). `TrainTroopSchedule` key is now (village, building, troop).
  `TrainTroopCommand` trains one planned troop; with "Train maximum number if low on resource" the amount is now capped to what the resources allow (before: the full amount was typed).
- Tests: `TrainTroopPlannerTest` (slots, legacy fallback, Great buildings, intervals, schedule). `VillageSettingInputTest` counts the new enums automatically.
- Verification: static only. NOT built, NOT live. XAML checked well-formed.

## 2026-10-27 — Upgrade building: no more 16-second retry loop when the game blocks a plan (code, NOT yet built or tested live)

Log A_1201 (second account, 2026-10-26 11:38-14:34): `Upgrade building` ran 678 times (every ~16 s, ~3,400 page loads) on one plan, Grain Mill to level 5.
A real capture of that contract page (user, 2026-10-26) shows why: the plan is unaffordable AND the game blocks it with the red line
"Increase crop production." (crop production is negative) - there is no countdown, so `UpgradeParser.GetTimeWhenEnoughResource` read 0 s,
`ExecuteAt` became "now" and the task ran again at once. The code was this way before; the 55-min waits seen on the other account came from pages
that did have a countdown.
- `Parsers/UpgradeParser.cs`: `GetBlockedMessage` (text of `.errorMessage`).
- `Commands/Features/Scheduling/UpgradeWaitRules.cs` (new, pure): a readable countdown always wins; otherwise a crop-production block waits 2-3 h; an unknown
  reason with no countdown waits 10-15 min (never "now").
- `Tasks/UpgradeBuildingTask.cs`: uses it in the MissingResource branch and logs why (`blocked by the game ("Increase crop production.") ... Looking again at HH:mm`).
  A crop-blocked plan does NOT set `ConstructionHold` (resources cannot unblock it, so "construction first" must not stop troop training for it).
- Tests: `UpgradeWaitRulesTest`, `UpgradeBlockedParser.Test.cs` + real fixture `Parsers/UpgradeBlocked/GrainMillCropBlocked.html`.
- Not changed / open: the blocked plan stays FIRST in that village's build list, so every other job of the village waits behind it (the user decides: remove the
  Grain Mill job or raise crop production first). The bot's existing "add a cropland upgrade" remedy (`LackOfFreeCrop`) did not fire here because the
  stored FreeCrop value passed the check; the page's own message is the better signal. Detection of the message is English only; other languages fall back to the 10-15 min wait.
- Also seen in the same log (not changed): Attack check stopped at 12:49:50 right after a `SaveAccountSettingCommand` at 12:52:08 (it removes the task when the
  "Attack check" checkbox is saved unticked - likely cause, unconfirmed); `TaskCanceledException` logged as ERR when Pause is pressed during a click delay.
- Verification: static only (brace/paren balance). NOT built, NOT live.

## 2026-10-26 — Troop training: per-building interval, "construction first"; Balance no longer loops silently (code, NOT yet built or tested live)

Log 2026-10-26 00:00-06:58 (build with the 10-25 evening fixes): Overflow fix verified live (12x `nothing worth sending`, task moved out);
Attack check 82 runs (242-387 s), no attack; no Start adventure line at all (user: hero adventures are switched OFF - nothing to fix).
Finding: 209 training batches / 11,677 Phalanx in 7 h (TS-3202 84 visits, TS-3205 77) while all 28 Upgrade building runs failed
for lack of Clay (140 lines; Marketplace/Barracks to level 20) - training always took the resources first.
User decisions: (A) Balance resources silent skip: same treatment as Overflow. (B) a village option "construction first / troops first";
(B2) a building must not be visited again before a minimum time even when its queue ends sooner, and that time is the training
interval - separately for each building (table row), not one value for the village.
- **Per-building interval:** new village settings `BarrackRepeatTimeMin/Max`, `StableRepeatTimeMin/Max`, `GreatBarrackRepeatTimeMin/Max`,
  `GreatStableRepeatTimeMin/Max`, `WorkshopRepeatTimeMin/Max` (minutes, appended last; default 0/0 = use the general
  `TrainTroopRepeatTimeMin/Max`, which stays as the fallback and is relabelled "General next visit after"). Village Setting tab:
  one "<Building>: not again before" row under each troop row; validator (min <= max, >= 0).
- `Tasks/TrainTroopTask.cs` rewritten around a per-building "not before" (`Commands/Features/Scheduling/TrainTroopSchedule`, memory only):
  buildings not yet due are not visited; after a trained batch next = max(queue end + 30-60 s buffer, the building's interval)
  (`QueueDeferRules.NextBuildingSeconds`); busy queue = its end + buffer; starved (not enough resources) = the interval; the task
  wakes for the earliest building. Behaviour change: a starved building no longer stops the other buildings of the visit (was `break`).
  A restart forgets the "not before" (everything due again); changing an interval takes effect after the current wait.
- **Construction first:** new village setting `ConstructionFirst` (checkbox, default OFF = troops first as before).
  `UpgradeBuildingTask` records `ConstructionHold` (until its own next run, capped at 3 h) when a plan lacks resources and clears it when
  a plan completes or nothing is waiting; with the setting on, `TrainTroopTask` of that village trains nothing during the hold
  (log: `construction first - a building is waiting for resources, no training until HH:mm:ss`). Limits: only the same village; resources
  that OTHER villages' training uses up (e.g. the hammer village supplying this one) are not protected - set that village to
  construction first too; a hold keeps training off for as long as the plan keeps lacking resources.
- **Balance resources** (`Tasks/BalanceResourceTask.cs`): silent `Skip.Error` exits (nothing overflowing, no target, amounts 0) now log the
  reason and move the task 20-30 min; a failed send-page open returns a warning + retry in 3-5 min instead of `Stop` (user pause stays Cancel).
- Tests: `QueueDeferRulesTest` (5 new `NextBuildingSeconds` cases). Enum-count test needs no change (all new keys are in `VillageSettingInput.Get()`).
- Verification: static only (brace/paren balance, XAML well-formed). NOT built, NOT live.

## 2026-10-25 (evening) — Live log review (14:17-17:34) + fixes: adventures keep running, Overflow no longer loops (code, NOT yet built or tested live)

Log 2026-10-25 14:17-17:34 (10-25 build, no attack happened): Attack check ran 40x (241-377 s apart, "no village is under attack");
queue-aware Train troop works (47 batches, `next visit at` lines; the `table.under_progress` assumption held); hero-health gate,
Trapper, Smithy, celebration not exercised; Dodge/Defend/Recall not exercised. Two real defects found:
- **Start adventure was dropped from the queue again (CLAUDE.md rule 16).** (a) 14:17:22 "No adventure available" = Skip.Error with
  ExecuteAt unchanged -> task removed. (b) 16:55:59 after a started adventure `NextExecuteStartAdventureTaskCommand` read the timer
  before the page showed it (0 s) -> `Schedule next run at 16:55:59` -> re-ran 5 s later -> "No adventure available" -> removed.
  Fix: `Tasks/StartAdventureTask.cs` turns a Skip from Explore into ExecuteAt +20-30 min and Ok; `NextExecuteStartAdventureTaskCommand`
  waits up to 6 s for the timer and, if still unreadable, uses +20-30 min (warning) instead of "now".
- **Overflow to hammer looped for nothing** (TS-3203, 18+ "failed / Bot skip this task" with no reason, Marketplace visit every ~80 s).
  Cause (from code): after the page was open, `amounts.Values.Sum() <= 0` returned a silent Skip (surplus or hammer room below one
  750 load after the 10-25 round-down), and `UpdateStorageCommand` re-adds the task after every storage update. Fix in
  `Tasks/OverflowToHammerTask.cs`: logs the reason, ExecuteAt +20-30 min, returns Ok. Also a failed send-page open ("Found 0 tabs",
  17:29) returned `Stop.Error` (rule 4); now a warning + retry in 3-5 min, Ok; a user pause still returns Cancel.
- Observed, not changed: TS-3203 trains small batches every 75-105 s while the hammer keeps supplying (by design); transient
  "page is navigating" ERR (16:39, `UpdateStorageCommand`) and "Execution context was destroyed" (14:58) recovered via Polly retry;
  60x "Construction queue is full" as WRN (noise); adventure info prints `unknown` for difficulty.
- Verification: static only (brace/paren balance). NOT built, NOT live.

## 2026-10-25 — Attack check, hero-health gate, queue-aware waits (code, NOT yet built or tested live)

- **Marketplace "no free merchants" race (2026-10-25, later; code, NOT built / tested live):** live log showed one village
  saying "No free merchants" 11 s after sending 4 of its 7 merchants' worth, ~170 such lines against 5 sends. A real capture of the
  Send tab (fixture `Parsers/SendResource/MarketplaceSendTab.html`) shows the parser reads it fine (7/7, 2250 per merchant; the
  direction marks around the digits are stripped), so the likely cause is reading the page before the script has rendered the
  merchant line (`GetFreeMerchants` returns 0 for "line missing"). `ToSendResourcePageCommand` now waits up to 10 s for the line
  (`SendResourceParser.HasMerchantInfo`) before the callers read it. Open: merchant load is 2250 on this account while
  `MerchantAmountRules` uses 750 (user decision pending: keep 750 steps or use the page's capacity).

- **Merchant loads (2026-10-25, later):** all merchant sends are now whole multiples of 750 (`MerchantAmountRules`; was 100).
  Surplus-driven sends (SendCrop, OverflowToHammer, BalanceResource) round DOWN; need-driven sends (SupplyFromHammer,
  SupplyForSettle, SupplyForRevive) round UP within what the source can spare/carry, so a small deficit is never rounded to 0.
  750 is hardcoded (Gaul); other tribes' loads differ. Not built / not tested live.
- **Queue parser (2026-10-25, later):** a REAL Barracks capture (fixture `Parsers/TrainQueue/BarracksActiveQueue.html`) showed
  the batch timers are cumulative, so `TrainQueueParser.GetSecondsRemaining` now returns the LAST `td.dur` timer (= end of the
  whole queue; the `tr.next` row is ignored), falling back to the first timer for Smithy/Town Hall tables. Trapper queue
  markup is still unverified. Test fix: `TrainQueueParserTest` was missing `using HtmlAgilityPack;`.

User decisions (2026-10-24 evening): (1) a lightweight attack look-out every 4-6 min (random) so auto refresh can be switched
off by hand; (2) training/building settings stay as they are; (3) hero below the adventure's minimum health -> no adventure and
no Oasis scout; (4) Smithy, celebration, trapper and troop training wait for the game-side queue END plus a small buffer.
The zip already held unwired helper files from the same evening (`AttackCheckTask`, `AttackCheckRules`,
`NextExecuteAttackCheckTaskCommand`, `QueueDeferRules`, `HeroHealthGate`, `TrainQueueParser`); `AttackCheckTask` referenced a
setting that did not exist yet, so that zip would not have compiled. This entry finishes and wires them.
- **Attack check**: new account setting `EnableAttackCheck` (default ON, appended last; Account Setting checkbox; toggled live in
  `SaveAccountSettingCommand`), case in `CustomServiceScopeFactory`, bootstrap in `RxQueue.AccountInitHandler` (both
  registration places). Every 4-6 min it reloads dorf1 and `AccountTaskBehavior` -> `UpdateVillageListCommand` reads every
  village's attack flag from the one village list and starts Dodge/Defend for a newly flagged village; no attack = it just
  re-arms. Auto refresh (`UpdateVillageTask`) is untouched.
- **Real bug found and fixed (`StartAdventureTask`)**: below the minimum health it returned `Skip.Error` with `ExecuteAt`
  unchanged, which `TimerManager` answers by REMOVING the task. Live log 2026-10-24: "Start adventure" ran once at 12:07 and
  never again, so adventures stayed off for good even after the hero recovered. Now it moves `ExecuteAt` +30 min, logs, returns Ok.
- **Hero gate**: `HeroHealthGate` (10-min shared reading, 30-min recheck, 10-min recheck after an unreadable page) +
  `HeroHealthReader`. `OasisScoutTask` does nothing (no page visits, no sends) when health < `MinHeroHealthPercent`; a health
  page that cannot be read counts as "not safe" for that run. `HeroReviveTask` feeds its own reading into the gate.
  The whole Oasis scout stops, including the plain-troop sends to empty oases (user: "oasis scout kapansin").
- **Queue-aware waits** (`QueueDeferRules`, `QueueWait`, `TrainQueueParser`):
  - `TrainTroopTask`: a building with a running queue gets no new batch, the task is moved to that queue's end + 30-60 s;
    an idle building is trained, then the countdown is read (<= 6 s wait) so the next visit is at its end; a starved/unreadable
    building keeps the normal repeat time; the earliest wait of all buildings wins; waits taken from the page are capped at 3 h.
  - `TrapTask`: at cap (only when max > 0, so an unread page is never "at cap") -> ~2 h (+0-10 min); running trap batch -> its
    end + buffer; otherwise normal repeat.
  - `SmithyUpgradeTask` / `SmallCelebrationTask`: running research/celebration -> exact end + buffer (was the bare end); after
    the visit the countdown is re-read; "not available, reason unknown" now waits 45-60 min (random) instead of a fixed 20 min
    (log 2026-10-24: a visit every 20-22 min per village). The log line now says whether the queue table was on the page.
  - `SmithyParser` / `TownHallParser` ongoing-timer reads keep the capture-verified path first and only add the wider markup.
- Tests: `QueueDeferRulesTest`, `AttackCheckRulesTest`, `HeroHealthGateTest`, `TrainQueueParserTest` (+ `files.txt`).
- Verification: static + the pure rule/gate tests (20/20) compiled and run on their own; the parser tests (`TrainQueueParserTest`)
  and the full harness could NOT run here (HtmlAgilityPack source download blocked). Task/handler code is brace-balanced and
  signature-checked only. NOT built, NOT live.
- Unverified assumption: the training / trap queue sits in `table.under_progress` with `span.timer` (no capture of an ACTIVE
  queue exists). If wrong, nothing is read and every path falls back to the normal repeat time (= old behaviour).
- Not changed (noted): after a Restart the DB flag `IsUnderAttack` can stay true while Dodge/Defend tasks are gone; the next
  check sees no NEW attack and starts nothing (pre-existing, TODO P2).

## 2026-10-24 (later) — Oasis scout: close the tile dialog, bounded navigation (code, NOT yet built or tested live)

Log 2026-10-24 00:00-07:55 (old build, before Defend pass 2): Defend/donation, Dodge and Recall all fine (6 Dodge+Recall
pairs, the first real attack at 01:49 included). New problem: 40 "Timed out after 180 seconds" lines = ~20 stalls of 3-9 min,
all in Oasis scout at `ToDorfCommand` ("Click timed out" -> 180 s URL wait, current URL `karte.php?x=..&y=..`). The user's
07:55 screenshot shows the oasis tile dialog still open, covering the top navigation bar at the window size in use. The same
tile worked at other times (02:22:00), so the trigger is intermittent; not proven.
- `Commands/Features/OasisScout/CloseTileDialogCommand.cs` (new): closes the dialog via its `.dialogCancelButton`
  (CSS `.dialogWrapper:has(#tileDetails) .dialogCancelButton`, 8 s), verifies with a 5 s wait, falls back to a JS click,
  never fails (only logs a warning).
- `Tasks/OasisScoutTask.cs`: calls it right after the tile data is read (and in the "no longer an unoccupied oasis" branch);
  `SendToOasis` opens the Send troops page under its own 45 s limit and returns Skip when exceeded (was up to 180 s per
  element, three attempts).
- Also in this zip: Defend pass 2 (see the 2026-10-24 entry above) so one zip carries both.
- Observed, not changed: 03:02:35 browser closed again after a click timeout (TS-3203 Train troop), cause unknown; Recall
  window margin: TS-3201 01:39:33 send -> 01:40:46 recall = 73 s of the 90 s window.
- Verification: static only (brace balance; no dotnet in the sandbox).

## 2026-10-24 — Defend: second pass 15 min before impact + wood diagnostics (code, NOT yet built or tested live)

Log 2026-10-23 15:57-20:18 (second raid build): Defend + donation, Dodge and Recall all worked for 4 raids
(Defend 17:38:14 and 19:11:03 -> trained 6 / 7 Phalanx, donated 7176 / 4498, daily counter 7176 -> 11674; Dodge+Recall
18:56:33->18:57:27 in TS-3202 and 18:57:44->18:58:41 in TS-3201, both inside the 90 s window; no browser close this
time). The wood fix worked (`donate1=2299`, `1505` donated although wood still reads -1). Defend saw the raids ~79 min
before impact, so everything produced in those 79 min stayed exposed.
User decisions: (1) page dump for the unreadable wood, (2) a second pass 15 minutes before impact.
- `Tasks/DefendDonateTask.cs`: `Task.Pass` + `ScheduleSecondPass`. After pass 1 (any outcome except a user pause) the same
  task instance gets `ExecuteAt = impact - 15 min` and returns Ok (Dodge's self-reschedule pattern), only if that is more
  than 60 s away. Pass 2 repeats the full sequence (train cheapest troop, donate everything left) under the same time
  budget; it does not schedule a third pass. Log: `... defending now (pass N, budget Xs)` and
  `Defend in V: second pass ... scheduled for HH:mm:ss, 15 min before impact`.
- Same file: when a resource reads as unreadable (-1) the warning now says what the node looked like
  (`#l1 missing` or `#l1 text='...' class='...'`) and the page (HTML + screenshot) is saved via `PageDump`
  (`*-resource-unreadable-<villageId>*`), at most once per hour. Send those files: the cause of the -1 is still unknown.
- Limits: pass 2 runs only when the bot is online at that moment (the attack wake window opens ~7 min before impact; a
  sleeping bot runs it then if the remaining budget is >= 25 s, otherwise it is skipped). Pass 2 trains from the DB stock
  (refreshed by Update village), a stale figure only makes the training step fail with a warning, the donation still runs.
- Observed, not changed: 17:03 Update village in TS-3203 stalled 3 min (click timeout -> 180 s URL wait); 20:12-20:14 nine
  click timeouts in the daily-quest reward. Defend never started in TS-3201 (setting probably off).
- Verification: static only (brace balance; no dotnet in the sandbox).

## 2026-10-23 (afternoon) — live log review of the raid build + wood fix (code, NOT yet built or tested live)

User ran the raid build (log 14:57-15:40). Verified LIVE: raid recognised ("Incoming raid … defending now"); Defend trained
74 + 4 Phalanx and the alliance donation WORKED ("Donated 11465 resources … daily total now 11465", 14:59:04; 486 at
15:10:20; counter rose 11465 -> 11951); Dodge on a raid with `DodgeRaidEnable` off logged "incoming movement(s) found but
none to react to" (no silent skip), with it on it sent at 15:08:07 / 15:24:08 and Recall cancelled at 15:09:11 / 15:25:02
(inside the 90 s window). One Dodge+Recall pair FAILED: TS-3201 sent 15:25:53, recall due 15:26:43, but the browser was
closed at 15:26:50 (click timeouts, "Browser was closed"), re-login, recall retried and found no cancel button at
15:28:43 (window gone); those troops were still outbound (arrive 15:43:54). Cause of the browser closes unknown (4 times
in 15:26-15:31, each after a Click timeout; the user pressed Pause at 15:40:29).
- Bug found: `StorageParser.GetWood` returned -1 on both defends (clay/iron/crop fine), so ~3.8k and ~160 wood stayed in
  the village and the log said "donated 11465 of 11464". Why the wood node is unreadable on that page is NOT known (no page
  capture). Fix in `Tasks/DefendDonateTask.cs`: an unreadable (negative) value is requested as "all"
  (`AskForAllAmount`); `DonateAllianceBonusCommand` already caps each resource at the alliance page's own maximum, so it
  cannot over-donate. A warning logs the raw values. The misleading "of {Stock}" was dropped from the Defend log line.
- Not changed: Recall timing (50 s) / resilience against a browser restart (TODO P1); Defend did not start for TS-3201 although
  it was attacked (most likely `DefendDonateEnable` is off there; not confirmed).
- Verification: static only.

## 2026-10-23 — Defensive tasks understand RAIDS; no more silent skips (code, NOT yet built or tested live)

Log 2026-10-22 16:12-19:50: Dodge + Defend started 14 times in TS-3202 and every time ended with a silent skip, so the
donation, Dodge and Recall were never exercised. Real Rally Point Overview captures (2026-10-23) show the cause class:
hostile incoming movements are two table types - `troop_details inAttack` ("X attacks Y") and `troop_details inRaid`
("X raids Y"); the countdown markup (`span.timer value=`) is identical. The old parser read only `inAttack`, while the
village "under attack" flag (dorf movements widget, `att*` icon) is also on for raids. A village that was only raided
therefore looked attacked but had "no attack" on the page. (Consistent with the log; the 10-22 page itself was never
captured, so this is the best explanation, not proof.)
- `Parsers/RallyPointOverviewParser.cs`: `IncomingKind {Attack, Raid}`, `IncomingMovement(Kind, Seconds, Headline)`,
  `GetIncomingMovements`, `GetIncomingHostileSeconds(doc, includeRaids)`; `GetIncomingAttackSeconds` unchanged (attacks only).
- `Tasks/DefendDonateTask.cs`: reacts to attacks AND raids (soonest movement); log says which kind.
- `Tasks/DodgeTroopTask.cs`: attacks always; raids only if the new village setting `DodgeRaidEnable` is on (default OFF,
  appended last to `VillageSettingEnums`; checkbox in the Village Setting tab; backfilled by `FillVillageSettings`).
  Defend's time budget only holds Dodge's send moment when Dodge will really react to that movement.
- `Commands/Features/DodgeTroop/IncomingMovementDiagnostics.cs` (new): when the flag is on but there is nothing to react
  to, the task now logs what the page really contained (or a warning "flagged but no incoming attack or raid") and saves
  the page via `PageDump` (at most once per village per 30 min).
- Tests: `RallyPointOverviewParser.Test.cs` + fixture `Parsers/RallyPointOverview/IncomingRaidAndAttack.html` (real capture:
  raid in 569 s + attack in 2364 s). Note the captured raid came from the user's OWN village (TS-3201, probably a test); an
  enemy raid should have the same classes with "?" troop counts (not seen yet).
- Not changed: the donation still has no live proof (TODO P0). Telegram text still says "saldiri".
- Verification: static only (brace balance, signature cross-check; no dotnet in the sandbox).

## 2026-10-22 (afternoon) — Defend: train cheapest troop, then donate everything left to the alliance Recruitment bonus (code, NOT yet built or tested live)

User decision (after the live-log review): on attack detection, once per attack: train the cheapest troop, donate ALL
remaining resources to the alliance's Recruitment bonus. Daily limit used up -> the rest stays in the village (no
merchant fallback). Single donation at detection. Reason: the Marketplace send never ran in 3 live attacks (17:47,
07:45, 11:06 all "No free merchants in 55031").
- `Tasks/DefendDonateTask.cs`: merchant steps removed; step 3 = `DonateAllianceBonusCommand` with its own 60 s cap
  (`DonationTimeoutSeconds`) inside the existing time budget; any failure is Skip; log line
  `Defend done … donated X of Y resources to the alliance Recruitment bonus`.
- `Commands/Features/DefendDonate/DonateAllianceBonusCommand.cs` rewritten against two real captures of
  `/alliance/bonuses`: clicks the bonus LABEL (the radio input may be hidden — prime suspect for the 2026-09-27 180 s
  hang, unproven), types amounts by id, waits max 10 s for the Contribute button to lose its `disabled` class (the game
  never really disables it; clicking while "disabled" silently does nothing), clicks it, then waits max 8 s for
  `donatedToday` to rise or the response text to appear. No sign -> logged as UNCONFIRMED (warning + page dump), not as
  failure. Any failure saves page HTML + screenshot. Optional `TimeoutSeconds` bounds the whole command.
- `Parsers/AllianceBonusParser.cs`: `GetRecruitmentBonusLabel`, `IsContributeEnabled`, `IsDailyLimitReached`,
  `GetDonatedToday`, `GetDonationResponseText`.
- `Services/PageDump.cs` (new): shared "save page HTML + screenshot to `Screenshots/`" helper.
- Tests: `MainCore.Test/Parsers/AllianceBonusParserTest.cs` + two real fixtures in `Parsers/AllianceBonus/`.
- 2026-10-22 14:50: a third real capture (form filled in, `Bonuses_Filled.html`) CONFIRMS the proof used by the command: with a bonus selected and amounts typed, `#donate_green` has no `disabled` class and `#donateSum` shows the typed total (321). New `GetDonateSum`; the command logs a warning if the page's sum differs from what it typed. The page also has per-resource `fillUp(...)` links ("max" next to each input) — an alternative to typing, not used. Still no capture of a FINISHED donation.
- 2026-10-22 14:52: the user's snippet right after a REAL donation of 321 shows the visible daily counter `span.donationValueNumber` going 0 -> 321 (of 1,650,000): the donation worked by hand and this counter is a confirmed success signal. New `GetDonatedTodayShown`; the command now treats the hidden `donatedToday`, the visible counter or the response text as confirmation. Whether the hidden input and `.bonus-donation-response` also update is still unknown.
- Findings from the captures: daily limit is 1,650,000 per player, so "limit reached" is unlikely to bind; per-resource
  maximum on the page = the active village's stock; `donate1..4` = wood, clay, iron, crop. Both captures show a fresh
  page (nothing selected, donated 0); **no capture of a finished donation exists** — the success signal is a guess.
- Unchanged: `SendResourceCommand`/`DefendResourceTargetRules` stay in the repo (used by other tasks / tests).
- Verification: static only (brace balance, signatures cross-checked; no dotnet in the sandbox).

## 2026-10-22 (10:36) — fix: Account Setting tab threw a cross-thread UI exception

Live log 10:35:59: `InvalidOperationException: The calling thread cannot access this object because a different
thread owns it` when opening the Account Setting tab. Cause: `AccountSettingViewModel.Load` runs on the task pool
(tab activation uses `ObserveOn(RxApp.TaskpoolScheduler)`) and set the four `[Reactive]` Telegram properties there;
the view two-way binds them, so ReactiveUI read the control's `ViewModel` from a non-UI thread. Fix: those four
assignments now run via `RxApp.MainThreadScheduler.Schedule`. File: `MainCore/UI/ViewModels/Tabs/AccountSettingViewModel.cs`.
Verification: static only. The other tab `Load` overrides were not audited (TODO P2).

## 2026-10-22 — fix: troop sends hung on element lookup (code, NOT yet built or tested live)

Cause (likely, from the 2026-10-21 log + code; no DOM capture yet): `SendTroopsCommand` located its form elements
with the raw positional `node.XPath`, so any shifted element made `GetElement` wait the full 180 s.
- `Commands/Features/SyncAttack/SendTroopsCommand.cs`: troop inputs by `input[name="troop[tN]"]`, event type by
  `input[name="eventType"][value="N"]`, X/Y/Send/Confirm by `StableXPath()`. New optional
  `Command.TimeoutSeconds` (hard limit for the whole send, linked CancellationTokenSource). On a timeout (own limit
  or a 180 s `GetElement` wait) it saves the page HTML + screenshot to `Screenshots/` and logs the paths; own-limit
  timeout returns `Skip`. A user pause is passed through untouched.
- `Tasks/DodgeTroopTask.cs`: send limited to `seconds-to-impact − 2` (skips if < 5 s); a failed send now logs a
  warning and returns `Skip` (no Retry holding the queue, no Recall queued without a movement); pause → `Cancel`.
- `Tasks/OasisScoutTask.cs`: oasis sends limited to 60 s (a stalled send is skipped, the loop moves on).
- `DefendDonateTask` + `SendResourceParser.GetMerchantsRawText`: when Defend skips with "No free merchants" it now logs the raw merchant block text and URL, so a parser false-negative can be told from a truly empty marketplace. No behaviour change.
- Same raw-XPath → `StableXPath()` fix in 7 more files: `ExploreAdventureCommand`, `HoldCelebrationCommand`,
  `ClaimQuestCommand` (2), `NpcExchangeToTargetCommand`, `NpcResourceCommand`, `SmithyUpgradeCommand`,
  `HandleUpgradeCommand`. No raw `By.XPath(x.XPath)` is left in `MainCore`.
- Verification: static only (brace/paren balance, signatures cross-checked; no dotnet in the sandbox).
  Live check: see TODO.md "Verification checklist".

## 2026-10-21 (evening) — analysis session, no code changed

- Analysed the live log 17:35–17:50 and the full Playwright branch; created `CLAUDE.md`, `PROJECT_CONTEXT.md`,
  `CHANGELOG.md`, `TODO.md` for this branch (none existed in the zip).
- Findings (details in PROJECT_CONTEXT.md §6.1):
  - Dodge scheduling, attack reservation, wake window: worked.
  - Defend: training worked (95 Phalanx); resource send skipped ("No free merchants").
  - Dodge send hung at `SendTroopsCommand`; impact passed unconfirmed; Recall never queued (unverified).
  - Oasis-scout sends to (39|-4) ×2 and (38|-7) hung the same way (`By.XPath(xNode.XPath)` 180 s timeouts).
  - Likely root cause: `SendTroopsCommand` still builds locators from raw `node.XPath` (6 sites) while the
    2026-10-21 `StableXPath()` fix was applied elsewhere. Not yet proven by a DOM capture.
- Verification: static only.

## 2026-10-21 — Playwright build runs

Reported by the user ("Oldu"): browser opens and the bot works. Fixes made on the way:
- `.github/workflows/*`: `include-hidden-files: true` on every `upload-artifact` step (hidden `.playwright` driver
  folder was missing from the zip).
- CI tests: `ArchitectureTest` exemption for `Tasks/Commands.Features`; internal enum counts in AccountSetting /
  VillageSetting input tests.
- `LoginCommand` logs browser-start exceptions.
- `ChromeBrowser`: Playwright `System.TimeoutException` handled everywhere (Click skipped its Force retry, Goto
  escaped raw — live log 13:01:09 / 14:12:51).
- `HtmlNodeXPathExtensions.StableXPath()` added (id-anchored XPath) and applied to SwitchTab/SwitchVillage and
  other commands; `SwitchTabCommand` wait predicate no longer throws while the tab bar is momentarily missing
  (live log 13:01:43 etc.). **Not applied to `SendTroopsCommand` and 8 other call sites (see TODO.md P0).**
- Verification: built + live (user).

## 2026-10-18/19 — Selenium → Playwright

Full replacement behind `IChromeBrowser` (`playwright-gocu-2026-10-19.zip`): `ChromeBrowser` rewritten on
Playwright 1.60 (persistent Chromium context per account, `PlaywrightAdapters.cs`, native proxy auth, click with
actionability + forced retry, 250 ms/180 s polling). User chose Playwright's own Chromium (extensions need it) and
left replace-vs-side-by-side to Claude. `packages.lock.json` must be regenerated after the package change.

## 2026-10-19/20 — Defend rework, attack reservation

- `DefendDonateTask` reworked: once per attack, train cheapest troop with whole stock, send everything left by
  Marketplace to another own village, hard time budget so Dodge is never delayed (`DefendAttackMemory`,
  `DefendResourceTargetRules`). The earlier alliance-donate version hung 180 s on the Alliance Bonus page.
- `AttackReservationRules` / `AttackReservationStore`: 3–5 min before impact until 15 s after, only defensive and
  session tasks run; defensive tasks get queue priority (log 2026-10-20 11:30–11:31 showed Recall pushed behind
  held-back tasks).
- 2026-10-20 diagnostics line `Defend send plan …` added after an attack sent only crop=8 although ~3.8k wood and
  ~36k crop were left.

## 2026-10-03 → 10-16 (condensed)

- Wake windows: night-raid (Sync Attack checkbox, 5–10 min before/after) and incoming-attack wake (default ON, no
  sentinel check-ins).
- ScoutAutoAttackTask (auto-send after scout report); ClaimDailyQuestRewardTask.
- Design discussions only: alliance-report target finder (paused), cross-village reinforcement on attack.

## Earlier (condensed from the Selenium era)

Sync Attack, Smithy upgrade, Auto Settle, Dodge (multi-slot, recall ~50 s after send), Raid List, Map Analysis,
Oasis Scout, Trapper auto-build, Crop Oasis scan, combat-decision engine Stage A (`CombatPower`,
`CombatDecisionRules`, `ReportTroopTableParser`, `ScoutedTargetGarrison`), ClaimQuest walks all tabs/pages
(2026-09-29), cookie-consent overlay dismissal (2026-09-04…09), dispatcher/bootstrap registration fixes
(2026-09-25).
