# PROJECT_CONTEXT.md — TravianBotSharp (Playwright branch)

Architecture and feature reference. Rules and gotchas live in `CLAUDE.md`; open work in `TODO.md`; history in
`CHANGELOG.md`. Last updated: 2026-10-21.

## 1. Status in one paragraph

The bot was migrated from Selenium to Playwright (2026-10-18/19, full replacement behind `IChromeBrowser`).
On 2026-10-21 the Playwright build started opening the browser and running for the user ("Oldu"). Live logs
now show login, adventure, daily quest, training, trapper, raid report, oasis scout and the defensive chain
(Dodge / Defend) all running. Open defects are concentrated in **element lookup by positional XPath**
(`SendTroopsCommand`), which made troop sends hang intermittently — including the Dodge send. Fixed in code 2026-10-22 (selectors by name/id/StableXPath, per-send time limit, failure dump); awaiting build + live proof. See TODO.md P0.

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
   checks the browser is open (relaunches if not), `CanStart`, online hours (`BypassOnlineHours` exempts
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

Economy / base: UpgradeBuilding (build templates), UpdateVillage/UpdateBuilding, TrainTroop (random Min–Max),
Trapper auto-build (random Min–Max, respects level cap), SmithyUpgrade, Demolish, Celebration, ClaimQuest +
ClaimDailyQuestReward (every 20–40 min checks the "!" indicator), NPC exchange, BalanceResource,
OverflowToHammer/SupplyFromHammer, SendCrop, AutoSettle (settlers, resource pull, auto-found; backup-coordinate
list NOT implemented), CompleteImmediately.

Hero: StartAdventure, HeroRevive (3-tier fallback) + SupplyForRevive.

Offence: Sync Attack tab (SyncAttackPlanTask, WaveAttackPlanTask, SendTroopsAtTimeTask incl. night-raid wake
window), Raid List (custom scheduler, per-row random resend, loot-percent troop scaling, account-wide min gap),
RaidReport (reads raid + scout reports; scout-report garrison cache `ScoutedTargetGarrison`), Combat-decision
engine Stage A (`CombatPower`, `CombatDecisionRules`, opt-in gate on Raid List; Stage B for real attacks not
wired), ScoutAutoAttackTask (auto-send after scout report, fixed source village, typed troop counts),
Oasis Scout (nearest-first; hero alone if animals weak, else troop slot amount), Crop Oasis scan (one-shot,
CSV), Map Analysis tab.

Defence: see §6.

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

Raw-XPath lookups; 3-minute waits on time-critical paths; AutoSettle backup coordinates; Raid List bulk-add dedupe
and row editing; combat-decision Stage B; unscoped village lookups; dead `SendReinforcementCommand.cs`.
