# TODO.md — TravianBotSharp (Playwright branch)

Priorities: **P0** = breaks a defensive/time-critical feature; **P1** = wrong or unverified behavior; **P2** = latent
risk / cleanup; **P3** = ideas. Updated 2026-10-24.

## P0

- [ ] **Verify the 2026-10-27 evening build live** (new): (A) a crop-blocked plan queues a cropland upgrade first (log `queued a cropland upgrade as the first job`), or with no cropland left logs `building tasks suspended`; (B) the per-troop tables show the right troops for the tribe, an old config appears as one row, two troops with different intervals are trained in one visit and come back at their own times. Build first: nothing here was compiled.
- [ ] **Verify the 2026-10-27 Upgrade fix live** (new): a plan blocked by "Increase crop production." must log `blocked by the game ... Looking again at` once and
  then stay quiet for 2-3 h (no 16-second `Upgrade building` loop); an unreadable wait logs `could not read the time until enough resources`.
  Decide: should a crop-blocked plan let the village's OTHER jobs run (today they wait behind it)? Check the Account Setting "Attack check" box after any save (an
  unticked save removes the task).

- [ ] **Verify the 2026-10-26 training change live** (new): set a non-zero "not again before" for the Barracks of a village, then
  after a batch the log must show `next visit at` no earlier than that interval (no more 75-105 s revisits). With "Construction first"
  on and a plan short of resources: `Train troop ... construction first ... no training until` and the building actually gets built.
  Also `Balance resources in V: nothing to send (...)` replaces the silent "failed / Bot skip". Decide later: protect a SUPPLYING
  village's resources for another village's building (today only the village's own training is held).

- [ ] **Verify the 2026-10-25 evening fixes live** (new): after an adventure the log must show a real next run (not `Schedule next run at <now>`),
  and `Start adventure: nothing to start now, looking again at HH:mm` instead of the task vanishing; `Overflow to hammer in V: nothing
  worth sending ... next look at` replaces the silent "failed / Bot skip" loop (no Marketplace visit every ~80 s).
  Still unproven from the same log: Attack check actually starting Dodge/Defend on a real attack; hero-health lines (needs
  `MinHeroHealthPercent` > 0); Trapper/Smithy queue reads.

- [ ] **Verify the 2026-10-25 build live** (new; also: merchant sends are multiples of 750, a busy Barracks defers to queue end, Trapper queue markup still unchecked): `Attack check:` lines every 4-6 min and Dodge/Defend still starting on a real
  attack with auto refresh OFF; `Start adventure: hero health ...` repeating every ~30 min while the hero is hurt and an
  adventure starting once it is healthy; `Oasis scout: hero health ...` skip lines; `Train troop ... queue is busy` /
  `next visit at` lines, and `Trapper ... at its level's cap` once per ~2 h. If a queue is never read (`queue table on the
  page: False` for Smithy/Town hall, or no `busy` line for training), send a page dump of a Barracks/Trapper page WITH a
  running queue - the `table.under_progress` markup is an assumption.

- [ ] **Verify the Oasis scout fix live** (new 2026-10-24): no more `Timed out after 180 seconds` at `ToDorfCommand` in the
  Oasis scout; a failed navigation now logs "opening the Send troops page took longer than 45s" and skips. If
  "the tile dialog could not be closed" appears, send a page dump (the close button selector is from one real capture).

- [ ] **Verify Defend pass 2 live** (new 2026-10-24): after pass 1 the log must show `second pass ... scheduled for HH:mm:ss`,
  then `defending now (pass 2 ...)` ~15 min before impact. Also look for `Screenshots/*-resource-unreadable-*.html` (why wood
  reads -1) and send it.

- [ ] **Verify raid handling live** (new 2026-10-23, see CHANGELOG). Needs a real hostile raid on a village with Defend on:
  log must show `Incoming raid on … defending now`; with `DodgeRaidEnable` off Dodge must log `incoming movement(s) found
  but none to react to`. If a flag is on and the page has neither table, `Screenshots/*-no-incoming-*.html` is saved:
  send it. Also still missing: a capture of an ENEMY raid (the one seen was the user's own, troop counts visible).

- [x] DONE LIVE 2026-10-23 (14:59 and 15:10, see CHANGELOG) — original item: **Verify the alliance donation live** (new 2026-10-22, see CHANGELOG). Trigger an attack on a village with
  `DefendDonateEnable`; log must show `Donated … to the alliance's Recruitment bonus` (or the UNCONFIRMED warning with
  saved page). If a `Screenshots/*-alliance-bonus-*.html` appears, send it: it is the missing capture of the real
  after-donation page, and the success signal (`donatedToday`/response text) should be confirmed or fixed from it.
  The visible daily counter is a confirmed success signal (user snippet after a real donation, 0 -> 321). A filled-in capture confirmed the "Contribute enabled" proof (2026-10-22 14:50). Open questions: does the label click select the radio? Does `Input` unlock the Contribute button? Does the page
  navigation keep the attacked village as the active one (the page max must equal that village's stock)?

- [~] **FIXED 2026-10-22 IN CODE, awaiting build + live proof** (see CHANGELOG 2026-10-22). Keep open until a live Dodge
  shows `Sent troops…` → `Dodged…` → `Recalled…`. If it still stalls, open the saved `Screenshots/*-sendtroops-*.html`
  to see the real cause (element hidden vs wrong selector).
- Original report — **Troop sends hang on element lookup (breaks Dodge → Recall).** `SendTroopsCommand` uses raw
  `By.XPath(node.XPath)` at 6 sites (troop inputs, X, Y, event radio, Send, Confirm). Log 2026-10-21: Dodge send
  at 17:49:00 never completed (impact 17:49:26); oasis sends to (39|-4), (38|-7) timed out with
  `By.XPath(xNode.XPath)`. Fix plan:
  1. Replace with direct selectors: `By.Id("xCoordInput")`, `By.Id("yCoordInput")`, `By.Id("ok")`,
     `By.Id("confirmSendTroops")`, `By.CssSelector("input[name='troop[tN]']")`,
     `input[name='eventType'][value='N']` (`By.Id/Name/CssSelector` exist in `BrowserTypes.cs`).
  2. Add a short timeout / fast-fail option to `GetElement`/`Wait` for time-critical callers (Dodge has ~30 s;
     a 180 s wait is useless there). On timeout dump page HTML + screenshot to confirm the real cause
     (positional XPath vs element not visible/enabled).
  3. In `DodgeTroopTask`, on send failure log it as Warning and still queue Recall only if a movement exists.
  Verify live: trigger a Dodge, check log for `Sent troops…`, `Dodged N troop type(s)…`, `Recalled dodge troops…`.
- [x] DONE 2026-10-22 (same bug class, converted to `StableXPath()`; still add the guard test below): `ExploreAdventureCommand:23`, `HoldCelebrationCommand:30`,
  `ClaimQuestCommand:81,95`, `NpcExchangeToTargetCommand:161`, `NpcResourceCommand:155`,
  `SmithyUpgradeCommand:42`, `HandleUpgradeCommand:196`.
- [ ] Add a guard test that fails on `By.XPath(<x>.XPath)` (needs source access; CI can grep the repo).

## P1

- [ ] **Recall can miss its 90 s window when the browser restarts** (live 2026-10-23 15:25-15:28, TS-3201). Dodge sent at
  :53, browser closed at :50 of the next minute, recall found no cancel button at 15:28:43. Ideas: recall earlier
  (setting `DodgeRecallSecondsAfterSend` 50 -> ~30-40), retry the recall quickly after a re-open instead of re-login first,
  and find why the browser closed (Click timeouts then "Browser was closed", 4x in 5 minutes).
- [ ] Wood read as -1 on the Barracks/after-train page (`StorageParser.GetWood`, node `l1`): get a capture of that page to find
  out why; the Defend donation now works around it (asks for "all").
- [ ] Dodge target (35|-5) is a ~9-18 min trip and an unoccupied oasis: troops left out when a recall fails will fight
  there. Consider a nearer/safer target (user setting, no code).

- [ ] **Recall has never been verified live on the Playwright build.** Needs a successful dodge send first (P0).
  Also check `RecallTroopCommand` uses `StableXPath()` (it does) and that the abort button is found within the
  90 s cancel window.
- [x] OBSOLETE for Defend since 2026-10-22 (Defend donates instead of sending by merchant). Still relevant for Balance/Overflow/Supply tasks. Earlier: diagnostics added (Defend logged the raw merchant block + URL when it skipped). Waiting for the next attack's log to decide. **Defend resource send: "No free merchants" may be a parser false-negative** (`GetFreeMerchants` returns 0 for both "none free" and "not found/unparsable"; no fixture test exists for `SendResourceParser`). 2026-10-20 attack sent only
  crop=8 despite large stock. The `Defend send plan` line never printed on 10-21 because it skipped earlier. Log
  `GetFreeMerchants` raw value and merchants-on-the-way; confirm with a village that certainly has merchants home.
- [ ] Defend budget constants (`MinimumActionTimeSeconds = 25`, Barracks ~10–15 s, Marketplace ~15–20 s) are
  estimates. 10-21 data: training took ~3 s (17:47:38→41); Marketplace nav ~4 s. Re-tune.
- [ ] Oasis scout: when a send fails with Retry, the task tries the next oasis and wastes 3 min each time; make a
  failed send skip the oasis quickly and log it.
- [ ] Pause during a 180 s wait is the only way out (user pressed Pause at 17:38:53 and 17:50:54). Shorter waits
  (see P0.2) fix the cause.
- [ ] Combat-decision engine: reveal-parsing of a "troops" scout report against a non-zero garrison unverified
  live; Stage B (real Attack wiring) not done.
- [ ] ScoutAutoAttackTask, ClaimDailyQuestRewardTask: 2026-10-06/07 stall after the 25-point reward (waited on the
  50-point screen) — cause never found; re-test on Playwright.

## P2

- [ ] After `Restart` (TaskManager.Clear) the DB flag `Village.IsUnderAttack` can remain true while Dodge/Defend tasks are
  gone; `UpdateVillageListCommand` / `UpdateStorageCommand` only start them for a NEWLY flagged village, so an attack that is
  already flagged is not handled. Needs a safe re-add (Dodge has no "already handled" memory, unlike Defend).
- [ ] Oasis scout also stops its plain-troop sends when the hero is hurt (user decision 2026-10-24); revisit if only hero sends
  should stop.

- [ ] Audit the other tab `Load(AccountId)` overrides (MapAnalysis, SyncAttack, EditAccount, Village, RaidList, WaveAttack,
  Farming, Debug) for `[Reactive]` property sets made on the task pool; marshal them with `RxApp.MainThreadScheduler`.
  Fixed so far: AccountSettingViewModel (2026-10-22).

- [ ] Unscoped village lookups (`Villages.FirstOrDefault(x => x.Id == …)` without `AccountId`) in
  `UpdateStorageCommand`, `SendResourceCommand`, `DodgeTroopTask:74`, `DefendDonateTask:44`, `BalanceResourceTask:173`,
  `HeroReviveTask:149`, `SupplyFromHammerTask:71`, `OverflowToHammerTask:52,140`, `SendReinforcementCommand:22` (10 hits).
- [ ] `SendReinforcementCommand.cs` is dead code (CHANGELOG once said deleted).
- [ ] `SyncBridge.Run` only converts `PlaywrightException`; a `System.TimeoutException` from `Locator.*` in the
  adapter would escape as a raw exception. Mirror the `ChromeBrowser` handling.
- [ ] `docs/ONBOARDING.md` and `.github/copilot-instructions.md` still say "Selenium".
- [ ] Regenerate `packages.lock.json` whenever packages change (CI restore fails otherwise).
- [ ] 2026-09-26 open observations (Selenium era, re-check on Playwright): TS32 login 180 s timeout (suspected
  night/"mobile version" page variant), TS12 hero-inventory item lookup 180 s (item type exhausted, no
  "genuinely absent" exit in the wait loop).

## P3 (ideas, not started)

- [ ] Auto Settle: ordered backup coordinate list.
- [ ] Raid List: dedupe on bulk add; edit Min/Max in place (today = delete + re-add, losing stats/schedule).
- [ ] Alliance "Attacks" report list as a raid-target source (oases + enemy villages) — paused by the user.
- [ ] Cross-village reinforcement (Phalanx, Druidrider, hero) arriving before impact, with wake window.
- [ ] Optional "sentinel" check-ins so an attack can be detected while the bot sleeps (user said no for now).

## Verification checklist after any defensive-feature change

1. Static checks + CI green. 2. Live: attack on a test village with `DodgeEnable` + `DefendDonateEnable` on.
3. Log must contain, in order: `Incoming attack … will send dodge troops`, `Defend … trained`, `Sent troops from
   village … arriving`, `Dodged … will recall`, `Recalled dodge troops`.
