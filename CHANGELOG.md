# CHANGELOG.md — TravianBotSharp (Playwright branch)

Newest first. "Verification" says how far a change got: static (read/checked only), built (CI green), live (user ran it).

## 2026-11-16 - Village setting tab: grouped, collapsed, quieter (XAML only, NOT yet built or tested live)

User request ("görsel olarak daha sade getirilebilir mi" -> analysis -> "yaz"): the Village setting tab was one long form of ~100 fields and ~20 feature boxes, all open, with 3-5 line italic paragraphs inside.
- `WPFUI/Views/Tabs/Villages/VillageSettingTab.xaml` rewritten as layout only. Five section titles (Village & building, Troops, Resources & logistics, Defence, Expansion & scouting); every feature is a COLLAPSED `Expander` inside the old `Box` border. A feature with a switch shows a green `● ON` in its header while the switch is ticked (Send crop: `RECEIVE` / `SEND`). The detail fields of a feature are hidden while its own switch is off (`Visibility` bound to the checkbox through `BooleanToVisibilityConverter`; the values stay in the view model and the database, saving is unchanged).
- Long explanations (Buy crop, Share train, Construction reserve, Queue horizon, Trap, Crop scan, Dodge, Defend, Auto settle, Auto balance) moved from italic paragraphs / very long checkbox texts into tooltips (explicit `ToolTip` elements, `MaxWidth` 420, no global ToolTip style); the checkbox texts were shortened, no meaning dropped.
- Deliberately NOT gated: "Troop training" (its Construction reserve is also needed by the share mode while Train troop is off), the two supply-reserve fields and the build-template box of Auto settle (they apply to this village as a source, not to settling), Upgrade settings (no master switch).
- Unchanged on purpose: all 100 `x:Name`s, `VillageSettingTab.xaml.cs` (bindings by name), enums, view models, validators, tests. Tab order of the boxes changed (Troop training moved ahead of the logistics boxes; "Village information" and "Feature settings" merged into one box; the one-time Demolish box moved next to the building boxes).
- Not done (next steps from the analysis): Account setting tab (same pattern, `ScoutAutoAttackTroop1..10` as a table), left-hand button panel (Delete away from Pause, confirmation), number codes as drop-downs (movement type, rebuild building, source village), "simple / advanced" switch.
- Verification: static only. XAML well-formed; the set of `x:Name`s equals the old file (100 = 100, no duplicates); every `ElementName` reference and every name used by the code-behind exists. Not compiled, not looked at on screen. If a section header's "ON" badge or a hidden field misbehaves, send a screenshot.

## 2026-11-15 - Resource ping-pong, idle-session reload, planned-wait logging (code, NOT yet built or tested live)

Live log 2026-11-15 00:01-11:35 (account with 3 villages, share training on in 22459): no ERR, 229 WRN, 2864 INF. Share-based training ran 18 visits without a fault (12 trained, 7 plans of 0 units because wood above the reserve was 0 while clay/iron/crop sat at 15-59k - both troops need wood, so this is the weights/stock, not a bug). What the log did show:
- **Balance <-> Overflow-to-hammer ping-pong** (04:18-11:30, ~20 shipments of 20-33k crop; 22459 -> 33411 by Balance, 33411 -> hammer 22459 by Overflow to hammer, e.g. 04:18 out 27,750, 04:22 back 29,250; 05:31 out 33,000, 05:53 back 29,250, 05:59 out 32,250). Cause (code): both tasks computed the receiver's room as capacity - stock, so each shipment filled the receiver to ~100 % - above its own outgoing threshold - and it shipped the load back. Same class as the 2026-11-04 observation (TODO P1). Fix: `ReceiveRoomRules` (pure): `CeilingPercent` = lowest outgoing percent the receiver has enabled (Auto balance overflow %, and Overflow to hammer % unless it is the hammer), `Room` = highest stock strictly below that percent. Balance uses it for the target choice (`GetBestTarget`, so `CanStart` is false when no village has real room) and the per-resource cap; Overflow to hammer uses the hammer's Auto balance percent for the cap AND in `CanStart` (queued only when at least one merchant load fits). Limit: shipments still in transit are not counted (two sends 10:04:26/10:04:45 went to the same receiver 19 s apart).
- **Overflow to hammer silent "failed / Bot skip this task"** (4x: 01:48, 02:29, 04:24, 05:29, no reason): the three early exits were `Skip.Error` with ExecuteAt unchanged. Now `Defer` (reason in an Information line + 20-30 min), hammer village lookup scoped by AccountId.
- **180 s stall after a long idle gap** (10:00:00 after the 05:59:52-10:00:00 gap: `SwitchVillageCommand` -> `Timed out after 180 seconds / villageChanged`, Retry, then `Account is logout. Re-login now`; same at 10:00 on 2026-11-02, TODO P1). The old in-game page stayed on screen, so `IsIngamePage` passed while the server session was gone, and the first click redirected to the login page. `AccountTaskBehavior` now reloads the page before judging it when the previous task of the account ended >= 30 min ago (`IdleSessionRules` / `AccountActivity`, memory only; not for `LoginTask`), logs `the account was idle for N min, reloading the page before the task`; a dead session then shows the login page at once and the existing re-login path runs.
- **Planned waits are no longer warnings** (190 of 229 WRN lines): `NextExecuteError` (Construction queue is full, prerequisite in queue) is logged as Information in `ErrorLoggingBehavior` (was 2 WRN per run) and `TimerManager` (`Task X waits: ...` instead of `Task X failed` + message). The next run was already set to the queue end (`Schedule next run at 00:20:48` after a 00:06 upgrade), only the log level was wrong.
- Files: new `Commands/Features/SendResource/ReceiveRoomRules.cs`, `Services/Browser/IdleSessionRules.cs`, tests `ReceiveRoomRulesTest` (7), `IdleSessionRulesTest` (4), both added to `tools/pure-tests/files.txt`; changed `Tasks/BalanceResourceTask.cs`, `Tasks/OverflowToHammerTask.cs`, `Behaviors/AccountTaskBehavior.cs`, `Behaviors/ErrorLoggingBehavior.cs`, `Services/TimerManager.cs`.
- Seen, NOT changed (open): Share train visits with a plan of 0 (7 of 18, ~5 s each, then a 10-20 min pause) when the bottleneck resource is 0 above the reserve; every upgrade is paid from the hero inventory (17 of 50 runs, TODO P1 decision pending); `Smithy ... no Improve button` and `Small celebration is not available` lines every ~50 min (no reason logged); adventure timer unreadable again (11:28) and distance `13.04` carries hidden marks; `Site at location N is at level -1` once.
- Verification: static only. The 11 new pure tests plus `MerchantAmountRulesTest` pass in the HAP-free harness (15/15); brace/paren balance of every touched file checked (AccountTaskBehavior's raw count differs by 2 in the ORIGINAL too - string literals with parentheses - and my change is balanced). Not compiled, not live.

## 2026-11-14 - Crash / dead-driver hardening + CI fix of the share-training test (code, NOT yet built or tested live)

Live log 2026-11-13 (10:29-15:55, analysed with the user): (1) the Chromium window closed on its own at 10:31 (log: `Process exited` at `browser.Html`, then 1 h 51 min without a single log line until the user logged in by hand 12:22), again at 12:25 (`Target page, context or browser has been closed`, reopened by itself in 15 s) and at 13:24:49 (`Process exited`); from 13:25:31 to 15:54:33 the bot tried to reopen the browser 6163 times, one attempt every ~1.4 s, every one failing at once with `Process exited`; (2) later the whole app died at 15:55:17 in the middle of an Oasis scout, no exception and no shutdown line in the log (the app had no global exception handler). What the log PROVES: the reopen loop never succeeded for 2.5 h (no Dodge/Defend/Attack check then) and the process ended without a trace. What it only SUGGESTS: "Process exited" is raised by Playwright .NET when its DRIVER process (node) exits, and `PlaywrightHost` caches that driver for the whole app (reset only in `ChromeManager.Shutdown`), so a dead driver fails every launch until the app restarts; the app crash may be an exception that escaped the async `Elapsed` handler of `TimerManager` (no try/catch, TODO P2) or an unobserved background exception - not proven, the Windows Event Viewer entry (.NET Runtime / Application Error around 15:55) would show it. Why the driver died is unknown (both first incidents were in Oasis scout map navigation).
- `Services/CrashLog.cs` (new): process-wide `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` handlers (the latter marks the exception observed) writing to `Logs/crash.log` (plain file, independent of Serilog) and Serilog; installed in `WPFUI/App.xaml.cs`, which also logs UI-thread exceptions (`DispatcherUnhandledException`, marked handled so the bot keeps running).
- `TimerManager.Start`: the `Elapsed` handler now has try/catch/finally. An exception that leaves `Execute` is written to `crash.log`, the queue is released (executing task back to Waiting, `IsExecuting` cleared), the account is PAUSED (a permanent fault must not spin at 10 ticks/s) and the loop keeps living (`timer.Start()` in `finally`). A cancellation just pauses.
- `ChromeBrowser.Setup`: a `Process exited` from `LaunchPersistentContextAsync` drops the shared driver (`PlaywrightHost.DisposeAsync`, now tolerant of a dead driver) and launches once more with a fresh one (Warning `The Playwright driver has exited ... starting a new one`). Open: if the dead driver left a Chromium behind that still holds the profile folder, the fresh launch can fail on the profile lock - not seen yet.
- `TimerManager.EnsureBrowserOpen` (now an instance method): the failure log shows exception type, first line and inner exception plus the attempt count; from the 4th failure in a row each attempt waits 5 s more (60 s max); ONE Telegram message after 10 failures in a row and another after 100 (needs NotifyOnPause); the counter resets on success.
- `LogSink`: one lock for the dictionary and the lists, `GetLogs` returns a copy (fixes the "Collection was modified" when the Debug tab opens, TODO P2).
- CI fix: `ShareTrainSlotsTest` lacked `using MainCore.Enums;` (the 10 build errors of the first Actions run); the 2026-11-13 share-training code itself compiled.
- NOT done on purpose: `SyncBridge.Run` still converts only `PlaywrightException` (TODO P1): a `System.TimeoutException` turned into a `BrowserException` would be classified as a dead browser by `AccountTaskBehavior` (not transient) and cost a re-login - needs its own error type first.
- Verification: static only (brace/paren balance of every touched file; the 20 share-training pure tests still pass). Not compiled, not live. Note: MainCore builds with `WarningsAsErrors=Nullable`.

## 2026-11-13 - Share-based troop training: a separate mode (code, NOT yet built or tested live)

User request (design discussion first, then "yaz"): split the stock above the construction reserve between the troops by shares. Decisions: share = percent of the surplus stock per resource; what a troop cannot use flows on to the others by the same weights ("waterfall"); no visit times and no unit amounts at all - only weights (0 = this troop is not trained); the new system is a MODE of its own so the old one can be switched back to; a queue ceiling per building; the trigger is the stock, not time.
- New mode switch, village setting `TrainShareEnable` (default 0 = the old behaviour, nothing changes on update). The old `TrainTroopTask` and its settings are untouched apart from two gates: `TrainTroopTask.CanStart` is false while the share mode is on, and `UpgradeBuildingTask.KickTrainTroop` skips the training kick then. `SaveVillageSettingCommand` switches the modes: on = removes the queued `TrainTroopTask` and queues `ShareTrainTask`; off = removes `ShareTrainTask` and queues `TrainTroopTask` when its `CanStart` passes. All old values stay in the database, so switching back needs no re-entry. A queued task of the other mode also ends itself in its handler (CanStart is NOT re-checked by TimerManager, see CLAUDE.md rule 19).
- `Tasks/ShareTrainTask.cs` (village task, secondary: 300 s budget, never Retry/Stop, any failure = Warning + "next look" 10-20 min, ends with ExecuteAt unchanged = removed, rule 16). Queued by `UpdateStorageCommand` after every storage read when `CanStart` passes (enabled, memory "not before" passed, reserve > 0, surplus >= trigger); not in `RxQueue.AccountInitHandler` (same lifecycle as Buy crop); registered in `CustomServiceScopeFactory` (rule 1). One visit = SURVEY pass (open every weighted building once: unit cost, unit time, queue seconds, input box present = trainable) + TRAINING pass (open each building that got units, train through the existing `TrainTroopCommand` with an explicit amount, read the queue after). Only the village's own stock above the floor is used: no NPC trade, no hero inventory, no `ConstructionFirst` hold. Great Barracks / Great Stable are not part of the mode. A missing building sets its weights to 0 (like the old mode disables a building).
- `Commands/Features/ShareTrain/ShareTrainRules.cs` (pure, 20 tests incl. memory): `Surplus` (stock above the `ConstructionReserveRules.Floor`), `ShouldRun` (surplus of all four resources >= trigger % of 3 x Warehouse + Granary), `GroupBudgetSeconds` (queue ceiling minus the queue already running), `Allocate`: round after round every troop that can still afford one unit and has queue room gets weight/sum of the pool per resource, takes the whole units that fit, the pool shrinks, repeat until nothing is added; when no share covers a whole unit but one still fits the pool, the heaviest troop takes it (500 rounds at most). Troops of one building share its queue budget. `NextCheck`: 3-5 min after a visit that trained, 10-20 min after one that did not (stops a visit on every page read; this is the only time element left).
- `ShareTrainSlots.cs`: the 10 weight keys `TrainShare<Infantry|Cavalry|Siege><slot>` (same slots as `TrainTroopSlots`, tribe relative), `GetWeights`. `ShareTrainMemory.cs`: memory-only "not before" per village.
- Settings (appended last): `TrainShareEnable`, the 10 weights, `TrainShareTriggerPercent` (default 20), `TrainShareQueueMaxMinutes` (default 360, 0 = no ceiling). UI: new box "Share-based troop training (separate mode)" above "Troop training settings" in the Village setting tab (checkbox, trigger, ceiling, three weight tables); validator: share mode needs `ConstructionReservePercent` > 0 (otherwise the mode would spend the whole stock). Without a reserve the task also does nothing at run time (Warning).
- Files: new `Tasks/ShareTrainTask.cs`, `Commands/Features/ShareTrain/*` (3), `UI/ViewModels/UserControls/ShareWeightsViewModel.cs`, tests `ShareTrainRulesTest`, `ShareTrainMemoryTest`, `ShareTrainSlotsTest` (CI only); changed `Enums/VillageSettingEnums.cs`, `Infrasturecture/Persistence/AppDbContext.cs`, `UI/Models/Input/VillageSettingInput.cs`, `UI/Models/Validators/VillageSettingInputValidator.cs`, `Commands/UI/Misc/SaveVillageSettingCommand.cs`, `Commands/Update/UpdateStorageCommand.cs`, `Services/CustomServiceScopeFactory.cs`, `Tasks/TrainTroopTask.cs`, `Tasks/UpgradeBuildingTask.cs`, `WPFUI/Views/Tabs/Villages/VillageSettingTab.xaml(.cs)`, `tools/pure-tests/files.txt`.
- Known limits / not done: shares drift from the table when a troop cannot use its part (that is the waterfall, by design); a troop whose unit time cannot be read is not limited by the queue ceiling (Warning); the crop upkeep of the trained troops is not guarded (use Buy crop / the reserve); stock read for the plan is the page header of the last surveyed building (database fallback for an unreadable value); the queue read after a batch is only for the Overview tab; no per-building ceiling, one value for all.
- Verification: static only. `ShareTrainRulesTest` + `ShareTrainMemoryTest` (20 tests) pass in the HAP-free harness; brace/paren balance of every touched file and XAML well-formedness checked. The task, view model, XAML, `ShareTrainSlotsTest` and the enum-count test (+13 keys, all in `VillageSettingInput.Get`) were NOT compiled. NOT built, NOT live.

## 2026-11-12 - Overview tab: Construction column (code, NOT yet built or tested live)

User request: the Overview tab does not show whether a village is building. Design discussed first, then "yaz".
- New column "Construction" after "Crop /h". Source: the `QueueBuildings` table (kept current by `UpdateBuildingCommand` on every dorf1/dorf2 read, same table the Build tab shows) - no extra page load, no memory store. `VillageOverviewViewModel.LoadRows` runs three small queries for all villages at once (running jobs, build-list size from `Jobs`, villages that have any `Buildings` row = "was read").
- Text from the pure `VillageOverviewRules.FormatConstruction` (+ record `ConstructionItem`): `Granary 7 until 14:48 (12 min)` (several jobs: `; `-joined, by end time), `idle`, `idle, N job(s) waiting` (build list still holds jobs: the "why is nothing being built" case), `?` (village never read). Jobs whose `CompleteTime` has passed count as idle because the table is only cleaned on the next village read.
- Files: `Commands/Features/VillageOverview/VillageOverviewRules.cs`, `UI/Models/Output/VillageOverviewRow.cs`, `UI/ViewModels/Tabs/VillageOverviewViewModel.cs`, `WPFUI/Views/Tabs/VillageOverviewTab.xaml`, tests in `VillageOverviewRulesTest.cs` (+5).
- Not done: a reason for "idle" (`ConstructionHold`, missing resources, crop block) - only the waiting-job count; user left that column open and "yaz" was taken as the single-column variant.
- Verification: static only. The overview pure tests (23, incl. the 5 new) pass in the HAP-free harness; the view model and XAML were NOT compiled. NOT built, NOT live.

## 2026-11-11 (evening) - Village overview tab: all villages of the account in one table (code, NOT yet built or tested live)

User request (design discussion first, then "yaz"): see every village's state at a glance - resources, troops, merchants - later widened to the training of Barracks / Stable / Workshop, the Smithy upgrade and the Celebration. Decisions: selected account only; low-cost reads (no extra page load in the normal flow, a "Read from game" button for a full read); troops and merchants as "at home / outside"; own-village reinforcement shown apart; "outside" is ONE column; Great Barracks / Great Stable left out.
- New tab "Overview" (`UI/ViewModels/Tabs/VillageOverviewViewModel.cs`, `WPFUI/Views/Tabs/VillageOverviewTab.xaml(.cs)`, row record `UI/Models/Output/VillageOverviewRow.cs`): one row per village. Wood/Clay/Iron/Crop with fill % and crop balance, Smithy and Celebration come from the database and the village settings (`SmithyUpgradeEnable/BusyUntil`, `SmallCelebrationEnable/BusyUntil`) and are always current. Troops at home, reinforcements of the player's other villages, troops outside, "Troops read" age, merchants (home / away of total) and the three training queues show what the bot saw last (`?` = not read since the app started). Rebuilt on tab open, on "Reload table" and every 15 s while visible (the grid is only touched when a cell changed). Registered in `AccountTabStore`, `MainLayoutUc.xaml(.cs)` (3 bindings, like every tab).
- `Commands/Features/VillageOverview/VillageOverviewStore.cs`: MEMORY-ONLY snapshots (troops, merchants, training queue end per building). Deliberately no database table: this project has no migrations and a new table needs a hand-written patch for every existing install (AppDbContext "schema patches"). `VillageOverviewRules.cs`: the text of every cell as pure functions.
- `Parsers/RallyPointTroopsParser.cs`: Rally Point > Overview by table classes/ids (language independent): `inReturn` returning, `out*` going out, plain table with `tr#unitRowAtTown` = in this village (`data-did == data-ort` own troops, same player but other `data-did` = reinforcement from an own village), plain table without it = stationed in other villages. The page shows only 10 movements per section ("Incoming troops (12)" with 10 tables, `a.openedClosedSwitch` "Show all", `filter=1` incoming / `filter=2` outgoing): `IncomingTruncated` / `OutgoingTruncated` tell. `SendResourceParser.GetMerchantCounts` (free AND total, null when unreadable).
- `Tasks/RefreshVillageOverviewTask.cs` ("Read from game"): one-shot account task, queued at the front by the tab, only while the account is online. Per village (100 s budget each): Rally Point overview (+ follows "Show all" when truncated, otherwise the snapshot is flagged incomplete and shown as ">= ..."), Marketplace merchants, queue end of each of Barracks/Stable/Workshop the village has. Secondary task: no Retry/Stop (rule 4), a failed village is a Warning and the next one goes on; ExecuteAt unchanged = removed after the run (rule 16, intended). Registered in `CustomServiceScopeFactory` (rule 1), NOT bootstrapped in RxQueue by design.
- Passive reads (no extra page): `ToSendResourcePageCommand` stores free/total merchants after every Marketplace visit; `TrainTroopTask` stores the queue length it read or computed (busy branch, after a batch, end of the horizon visit). Troops are only read by "Read from game" for now.
- Tests: `RallyPointTroopsParserTest` (two new fixtures `Parsers/RallyPointOverview/TroopsStationed.html`, `TroopsMoving.html` = the real captures: 319 home + 112 stationed; 288/705/587 Swordsman at home/out/back, 291 Phalanx reinforcement, both sections truncated), `SendResourceParserTest` (+2), `VillageOverviewRulesTest` (14), `VillageOverviewStoreTest` (4); `tools/pure-tests/files.txt` updated.
- Not done / assumptions: `outAttack` / outgoing-reinforcement class names assumed `out*` like `outRaid`; the page that `filter=1` shows may hide the other section (then the outgoing number stays flagged incomplete); foreign players' reinforcement in the village is ignored; troops are not refreshed passively (the Send-troops tab, read by Raid List / Oasis scout, only has the home counts); Smithy shows "idle" when the busy-until is 0 (the setting cannot tell "idle" from "never looked").
- Verification: static only. The 18 pure tests (`VillageOverviewRulesTest`, `VillageOverviewStoreTest`) ran in the HAP-free harness (all pass). The parser, task, view model and XAML were NOT compiled: HtmlAgilityPack could not be downloaded (CLAUDE.md section 4), so the parser was only syntax-checked against a stub of the HAP API and its classification was replayed on both fixtures with a script (numbers above match). XAML well-formedness and brace balance checked. NOT built, NOT live.

## 2026-11-11 - Raid list: a troop shortage defers only that row (code, NOT yet built or tested live)

Live log 2026-11-11 00:27 (analysed with the user): `Raid list: village 71146 doesn't have enough troops in slot 4 (needs 43, has 28) - pausing the whole raid list (2 active row(s))`. Two rows share one set of troops (43 each, about 71 in the village), so the second row found 28 at home; the 2026-08-25 behaviour switched every row off for good and nothing switched them on again - the list stayed off for 5+ hours. User: "yaz" (defer only the row).
- `Commands/Features/RaidListScheduling/RaidListShortageRules.cs` (pure, memory-only streak per row, 4 unit tests): a shortage (troops or hero) now moves only that row's `NextExecuteAt` / `task.ExecuteAt` 8-14 min ahead (Information log `this row ... waits until ... (try n of 6)`). After 6 shortages in a row for the same row (about an hour) the whole list is paused and the Telegram message sent exactly as before (the alert the user wanted in 2026-08 is kept for a shortage that stays). A successful send resets the streak; pausing resets it too.
- `Tasks/RaidListTask.cs`: both `PauseWholeListAndNotify` call sites go through the new `DeferRowOrPauseList`; class comment updated.
- NOT changed: the cause in this village (slot 4 is not trained - no Stable visit in the log; a village setting, not code).
- Verification: static only; `RaidListShortageRulesTest` passes in the HAP-free harness; the task itself is not compiled. NOT built, NOT live.

## 2026-11-10 - Buy crop on the Marketplace when the granary runs low (code, NOT yet built or tested live)

User request (design discussion first, then "yaz"): solve the crop shortage with the Marketplace "Buy" tab; purpose = troops must not starve, even temporarily. User decisions: independent of the NPC exchange; the construction reserve is ignored; the longest trip is the user's setting; starvation ETA, several offers per run, payment order by fullest store and the in-transit memory exactly as proposed.
- New `BuyCropTask` (village task, secondary, 120 s budget, never `Retry`/`Stop`; any failure = Warning + "next look" 10-20 min). Queued by `UpdateStorageCommand` (after the NPC block) and by `SaveVillageSettingCommand` (`BuyCropEnable`); NOT in `RxQueue.AccountInitHandler` by design (same lifecycle as Smithy / NPC). Registered in `CustomServiceScopeFactory`. A village without Marketplace disables the setting (like NPC).
- Trigger (`CanStart`): `BuyCropEnable` and (crop + crop still travelling) < `BuyCropGranaryPercent` of the Granary and the "not before" time has passed. Buys until `BuyCropTargetPercent`.
- `Commands/Features/BuyCrop/BuyCropRules.cs` (pure, 20 unit tests): per row - must offer crop, 1:1 or better, Accept button enabled, merchants within the free ones, trip within `BuyCropMaxMinutes` (0 = no limit), no granary overflow (stock + in transit + offer). Order: offers that arrive before the crop runs out (`Storage.FreeCrop` < 0 -> `TimeToEmpty`, 2 min margin) first; shortest trip first; offers within 5 min of the shortest are equal and then the paying resource whose store is fullest wins, then the largest offer that fits the need (else the smallest overshoot). **Refinement of what was agreed:** when NO offer arrives in time (or the store is already empty) the nearest one is taken anyway with a Warning, otherwise the feature would do nothing exactly when the crop is already gone.
- `BuyCropCommand`: up to 3 offers per run, each chosen from a fresh read. Before clicking it re-reads the table and checks that the row still holds the chosen offer (`MarketplaceOffer.Signature`: the list re-renders and another player may take it). Confirmation = the free-merchant count dropped or the row count of that signature dropped (the "Offer successfully accepted" toast is not in the DOM). Unconfirmed = Warning + page dump `buycrop-unconfirmed`, nothing remembered. Also makes sure the filters are "crop only" and "1:1 or better" (clicks if not; the rules still protect when a click does not take). Pages: up to 3, stops when a page ends beyond the trip limit.
- `ToMarketplaceBuyPageCommand`: dorf2 -> Marketplace -> the Buy tab found by its link (`href` ends with `t=1`, not by position), with short waits of its own (rule 9).
- `BuyCropMemory` (memory only): crop bought and still travelling (counts as stock, so a 6 h trip does not cause a purchase every few minutes) and the per-village "not before" time (CanStart holds the task back; otherwise an empty offer list would mean a Marketplace visit every minute). Lost on Restart/app restart (accepted).
- Parser `MarketplaceBuyParser` + `MarketplaceOffer` (no HAP dependency in the record). Facts taken from the real TS12 captures (Marketplace 10): `#marketplaceBuy`, `.merchantsInformation` ("6/10", capacity 975), filters = `div.filterGroup` 0 (search, `data-option` 4 = crop) and 1 (ratio), rows `table.offers tbody tr` with `td.offered/wanted/merchants/duration/player/accept`; the Accept button is greyed out by the CSS class `disabled`, and the merchants cell gets `notEnough` when more merchants are needed than are free; amounts carry invisible direction marks (stripped by `ParseLong`). Language independent.
- Settings (appended last, all in `VillageSettingInput.Get`): `BuyCropEnable`, `BuyCropGranaryPercent` (default 20), `BuyCropTargetPercent` (60, saved never below the trigger), `BuyCropMaxMinutes` (120, 0 = no limit). Village setting tab: new box "Buy crop on the Marketplace".
- Files: `Enums/VillageSettingEnums.cs`, `Infrasturecture/Persistence/AppDbContext.cs`, `UI/Models/Input/VillageSettingInput.cs`, `WPFUI/Views/Tabs/Villages/VillageSettingTab.xaml(.cs)`, `Commands/UI/Misc/SaveVillageSettingCommand.cs`, `Commands/Update/UpdateStorageCommand.cs`, `Services/CustomServiceScopeFactory.cs`, new: `Tasks/BuyCropTask.cs`, `Commands/Features/BuyCrop/*` (4 files), `Parsers/MarketplaceBuyParser.cs`, `Parsers/MarketplaceOffer.cs`; tests `MarketplaceBuyParserTest` (+ fixtures `Parsers/MarketplaceBuy/Before.html`, `After.html` cut from the captures), `BuyCropRulesTest`, `BuyCropMemoryTest`; `tools/pure-tests/files.txt`.
- Known limits: the merchants stay away for the round trip (a 6 h offer blocks them for ~12 h: Defend / Balance / Overflow have fewer); the arrival time is taken as now + the shown trip time; one purchase can be repeated after a Restart (memory lost); a paying resource is not protected (reserve ignored by decision).
- Verification: static only. The HAP-free harness (`BuyCropRulesTest`, `BuyCropMemoryTest`, 24 tests) passes. `MarketplaceBuyParserTest` could NOT be run (HtmlAgilityPack download refused, CLAUDE.md section 4); its expectations were checked against the fixtures with a script only. Handlers / task / view model not compiled. NOT built, NOT live.

## 2026-11-08 - Catapult target for Sync Attack and Wave Attack (code, NOT yet built or tested live)

User request: catapult target for Sync Attack / Wave Attack / Raid List. The user sent the real `<select name="troops[0][catapultTarget1]">` of the confirmation screen. A first version put it in the Raid List form; it was REMOVED the same day (user decision): the Raid List sends `AttackRaid` and catapults do not fire in a raid. Raid List files are back to their previous state (a build that already ran the first zip may have an unused `CatapultTarget` column in `RaidListEntries`; harmless).
- `Enums/CatapultTargetEnums.cs`: the option values of that select (Main Building 15, Warehouse 10, ... Random 99; 0 = not set). Append only.
- `Parsers/RallyPointSendTroopsParser.cs`: `GetCatapultTargetSelect`, `HasCatapultTargetOption`; fixture `RallyPointSendTroopsParser_CatapultTarget.html` + 4 tests in `RallyPointSendTroopsParserTest`.
- `Commands/Features/SyncAttack/SendTroopsCommand.cs`: new last parameter `CatapultTarget` (default 0 = every caller unchanged). Selected on the confirmation screen right before the final click; a missing drop-down or a value the page does not offer is a Warning and the send goes on with Random, never a failure.
- `UI/Models/Output/CatapultTargetItem.cs`: ComboBox item list.
- Sync Attack and Wave Attack (user: "mancinik hedefi yok burada"): a "Catapult target" combo next to the movement type; the value is one per plan (`SyncAttackPlan.CatapultTarget`, `WaveAttackPlan.CatapultTarget`, both optional last record parameter) and travels `SyncAttackPlanTask`/`WaveAttackPlanTask` -> `SendTroopsAtTimeTask.Task.CatapultTarget` -> `SendTroopsCommand`. The probe send (Confirm=false) never sets it. A village/wave without catapults just has no drop-down: Information log, no failure. Files: `DTO/SyncAttackPlanDto.cs`, `DTO/WaveAttackPlanDto.cs`, the two plan tasks, `SendTroopsAtTimeTask.cs`, `SyncAttackViewModel`, `WaveAttackViewModel`, `SyncAttackTab.xaml/.cs`, `WaveAttackTab.xaml/.cs`.
- Rally Point level (blog.travian.com "The use of catapults"): 1 = random only, 3 = Warehouse/Granary, 5 = + resource buildings, 10 = almost everything, 20 = two targets. The code checks what the page offers, not the level. One target only: no capture of `catapultTarget2`.
- Not done: `catapultTarget2` (Rally Point 20), per-village target in Sync Attack.
- Verification: static only (brace balance, XAML untouched). The parser tests could NOT run (HtmlAgilityPack download refused, CLAUDE.md section 4). NOT built, NOT live.

## 2026-11-07 (late) - Troop training looks at the leftovers right after a building was started (code, NOT yet built or tested live)

User request: after a building is paid, what is left should go to troops at once instead of waiting for the training task's own schedule (hours away with intervals / queue end / horizon).
- `Commands/Features/Scheduling/TrainKickRules.cs` (pure, unit tested, 3 cases): `KickTime` = now + 10-20 s, or null when the task already runs that soon.
- `Tasks/UpgradeBuildingTask.cs`: after `HandleUpgradeCommand` succeeds (next to `ConstructionHold.Clear`), `KickTrainTroop` moves the village's `TrainTroopTask` to that time through `ITaskManager.AddOrUpdate` (only when `TrainTroopEnable` is on, only EARLIER, wrapped in try/catch so a failure never fails the build). New handler parameters `AppDbContext`, `ITaskManager`. Also removed a duplicated `using` that the previous entry had introduced.
- It only says "look now": per-troop / per-building intervals (`TrainTroopSchedule`), a busy queue, the horizon and the construction reserve still decide at that visit. User decisions: every village (not only `ConstructionFirst`), intervals NOT bypassed, independent of the reserve (with the reserve off a kick can leave the next building short; turn the reserve on for such villages).
- Verification: static only. `TrainKickRulesTest` + the earlier rule tests (21) pass in the HAP-free harness; brace/paren balance checked. NOT built, NOT live.

## 2026-11-07 (evening) - Construction reserve: troop training keeps a floor for the build plan (code, NOT yet built or tested live)

User request (design discussion first, then "yaz"): training ate every resource and Upgrade building starved (TODO P1 "Hero inventory eats the build budget", "trickle batches"). A true 60/40 production split was rejected as too stateful; a stateless FLOOR was chosen.
- New village setting `ConstructionReservePercent` (0 = off, default; percent of the storage capacity, per resource: Warehouse for wood/clay/iron, Granary for crop). Appended last in `VillageSettingEnums`, default in `AppDbContext`, field in `VillageSettingInput` (clamped 0-100), `AmountInputUc` in the village setting tab (Troop training box).
- `Commands/Features/TrainTroop/ConstructionReserveRules.cs` (pure, unit tested): floor = max(percent of capacity, cost of the next building), never above the capacity; training may only spend the stock above the floor. User decision: while the build queue still runs for more than 30 minutes (`RelaxMinutes`) only the percent part holds, the cost part counts again once 30 minutes or less are left.
- `Commands/Features/Scheduling/ConstructionNeed.cs` (memory only, like `ConstructionHold`): `HandleResourceCommand` stores the cost of the plan it reads off the build page; `UpgradeBuildingTask` stores "build queue full until T" (`NextExecuteError` with `PrerequisiteLevel == 0`), clears the slot-busy note when a plan is found, and clears everything when nothing is waiting (no jobs / other failure). The cost of a just-built plan stays as the estimate for the next one; it expires after 6 h. After a Restart nothing is known and only the percent part applies.
- `Tasks/TrainTroopTask.cs`: the floor is computed once per visit (`GetReserveFloor`, Information log with floor, cost and queue-busy minutes). Plain flow: the amount is cut to what the spendable stock pays for (`ReserveUnits`); a smaller amount is only trained when "Train maximum number if low on resource" is on, otherwise the troop is treated as starved (own retry time). Horizon flow: `MakeUpResources` now takes the floor and returns the number of units the caller may train; with a floor the village's own stock counts only above it, NPC trades are SKIPPED (they would redistribute the protected stock), the hero's inventory still adds on top of the spendable stock; a reduced amount counts as "starved" for `PlanNextVisit`. Without a floor every path is unchanged (same return values).
- Not covered by design: a true split of the spending, hero/NPC use by construction (`HandleResourceCommand` still drains the hero), other consumers of the stock (Balance/Overflow/Supply, Smithy, Celebration, Trapper). The floor does not protect against those.
- Tests: `ConstructionReserveRulesTest` (10 cases) added to `tools/pure-tests/files.txt`; the enum-count assertion of `VillageSettingInputTest` still holds (new key is in the input dictionary).
- Verification: static only. `ConstructionReserveRules` + `TrainResourceRules` tests (18) ran in the HAP-free harness (all pass); brace/paren balance of every edited file and XAML well-formedness checked. NOT built, NOT live.

## 2026-11-07 - Smithy can no longer pause the account; new-village template wakes Upgrade building; hero resources on by default for new villages (code, NOT yet built or tested live)

Live log 2026-11-06 17:24-17:56: `SmithyUpgradeCommand` found no research block for slot 2 in 71146 (read ~1 s after the navigation; the same slot was found 10 times on 2026-11-05) and returned `Stop` - `SmithyUpgradeTask` also turned every failure into `Stop` - so the account was paused (no Attack check, Defend, Dodge, daily quest) until the user resumed.
- `Commands/Features/SmithyUpgrade/SmithyUpgradeCommand.cs`: when the research block is missing it waits up to 8 s for it (linked token, a user pause still returns `Cancel`), then writes the Warning + page dump `smithy-no-research-block` and returns `Skip`; every other `Stop` became `Skip`.
- `Tasks/SmithyUpgradeTask.cs`: whole run under a 90 s budget (`RunAsync`); a failure other than a user pause, `BrowserClosed` or `MissingBuilding` sets `SmithyUpgradeBusyUntilUnixTime` 10-20 min ahead, logs a Warning and returns Ok (the task ends with ExecuteAt unchanged and `UpdateStorageCommand` queues it again once CanStart passes - same mechanism as the normal "nothing to do" path). A timeout also dumps `smithy-timeout`.
- `Tasks/ApplyBuildTemplateTask.cs`: after the template jobs are written it calls `taskManager.AddOrUpdate<UpgradeBuildingTask.Task>` and raises `JobsModified`. Before, nothing queued Upgrade building for a village founded during a session (the account-init scan only sees villages that already had jobs; `JobsModified` is handled by a view model) - the template waited until the next login/Restart. Found by code reading; the log of an affected village was not available.
- `Infrasturecture/Persistence/AppDbContext.cs`: `UseHeroResourceForBuilding` default 0 -> 1 (user request). `FillVillageSettings` only fills MISSING rows, so existing villages keep their value; only villages founded from now on get it. `HeroResourceReserve` still applies to training only (open, TODO P1 "Hero inventory is the build budget").
- Not done: catapult target for Sync Attack (the repository holds no capture of the confirm page with catapults, so the selector would be a guess).
- Docs: TODO (P0 entry, 2026-11-07 verify item, new P1 findings from the 11-05/11-06 logs), PROJECT_CONTEXT date, CLAUDE.md rule 4 pointer.
- Verification: static only (brace/paren balance, signature cross-check). NOT built, NOT live.

## 2026-11-05 (night) - Documentation cleanup after a read-only code analysis (docs only, no code changed)

Static read of the 11-05 changes, `TimerManager`, the browser adapters and `NoRawXPathTest`; nothing compiled or run.
- `TODO.md`: removed the `fixes-2026-10-29.zip` item (no zip in the repository root); `SyncBridge.Run` `System.TimeoutException` moved from P2 to P1 with the reason (all `PlaywrightElement` reads use it; a raw exception pauses the account); three new P1 findings: essential buildings in new villages (Warehouse/Granary forced to level 3), Update village loading dorf2 on every run, lost-village detection trusting a single read.
- `PROJECT_CONTEXT.md`: "Last updated" now 2026-11-05.
- Verified in code while reading: no raw `.XPath` left in `MainCore`; `TimerManager.Start` still has no try/catch; StartAdventure deadline maps to `Cancel.Error` in `ChromeBrowser` and is told apart from a user pause correctly.
- Verification: static only.

## 2026-11-05 - Live-log fixes: adventure loop, Defend without Rally Point, lost villages, SendResource selectors, essential buildings (code, NOT yet built or tested live)

Live logs 2026-11-04 (00:00-05:00 and 10:03-10:18, plus the adventure-dialog screenshot). Finding 1: the hero's home village lost its Rally Point, the game greyed the Explore button out (CSS class only, rule 11), the bot clicked it, waited 180 s for Continue, Retry x3 -> the account was paused (which also stops Attack check and Defend). User decisions: items 1-4 as recommended, Attack check does NOT bypass offline hours (NO), SendResource XPath fix, essential-building guard.
- `Parsers/AdventureParser.cs`: `IsExploreDisabled(button)` (disabled attribute or class) and `GetBlockedMessage(doc)` (best-effort text of the game's red warning; heuristic, used for logs only).
- `Commands/Features/StartAdventure/ExploreAdventureCommand.cs`: missing button -> `Skip` (was Retry); disabled Explore -> page dump `adventure-blocked` + `Skip`.
- `Tasks/StartAdventureTask.cs`: never Retry/Stop. 90 s deadline around the old body (`RunAsync`); a failure or timeout sets `ExecuteAt` first: "No adventure available" 20-30 min (as before), other Skip 45-60 min (Warning), timeout 15-25 min (page dump `adventure-timeout`). User pause, browser closed and Stop pass through.
- `Tasks/DefendDonateTask.cs`: wood -1 fix - one resource-bar snapshot (`ReadResourceBarAsync`, waits up to 5 s until wood/clay/iron/crop all parse) instead of four live reads right after the training POST. No Rally Point (`MissingBuilding`) no longer ends silently: warning, `DefendAttackMemory.MarkHandled` for 20 min, one train + donate run under a 120 s budget (`RunWithBudgetAsync`, also used by the normal path).
- `Commands/Features/DefendDonate/DefendAttackMemory.cs`: `Clear(villageId)`.
- `Commands/Update/UpdateVillageListCommand.cs`: villages of the account that are no longer in the parsed list (conquered/destroyed) lose their queued, non-executing village tasks and their Defend memory; Warning line `is no longer in the village list ... removed it and N queued task(s)`. Ends the "Village not found" loop.
- `Commands/Features/SendResource/SendResourceCommand.cs`: every lookup is now an id/name/CSS selector (`form input[name="..."]`, `form button[type="submit"].send`, scoped to `#marketplaceSendResources` when present); no `node.XPath` left. Cause: live ERR 2026-11-04 02:48, `ClearAllResourceInputs` positional XPath, 30 s `TimeoutException`.
- `Commands/Features/UpgradeBuilding/EssentialBuildingRules.cs` (new, pure) + `EnsureEssentialBuildingsCommand.cs` (new) + `Tasks/UpdateVillageTask.cs`: Update village now re-reads dorf2; a missing Main Building (slot 26 is a Site), Warehouse or Granary (first free Site in 19..39 except 26/39) is queued to level 3 at the TOP of the build list (Warning `has no <type> (destroyed?)`), and the Upgrade building task is woken. The merged layout (built + under construction + queued jobs) is used, so nothing is queued twice; an unscanned layout (< 18 infrastructure slots) never counts as "missing".
- Not changed: Attack check still does not run in offline hours (user decision). Balance ping-pong, hero resource reserve, `SyncBridge.Run` `TimeoutException`, `TimerManager` try/catch, `LogSink`, Stop in secondary tasks stay open (TODO).
- Tests: `AdventureParser.Test` (+5), `DefendAttackMemoryTest` (+1), `EssentialBuildingRulesTest` (new, 8), `NoRawXPathTest` (new guard: fails on any raw `.XPath` property use in `MainCore/**/*.cs`; closes the TODO guard-test item). CI compile errors from missing usings in the two test files were fixed the same day.
- Verification: static only (brace/paren balance, signature cross-check). NOT built, NOT live. Expected in the next log: no Retry/pause from Start adventure, `Explore button is disabled` + 45-60 min defer when the Rally Point is missing, Defend trains in a village without Rally Point, no "Village not found" after a village is lost, SendResource without 30 s timeouts, `queued ... at the top of the build list` only when a building really is missing.

## 2026-11-02 (evening) - Oasis scout: troops can attack oases WITH animals (code, NOT yet built or tested live)

User: "vahsi hayvanlarin oldugu vahalara kahraman disinda bir saldiri olmuyor" - by design the animal branch only had the hero (alone, under the safety threshold); once the hero was away (`heroAway`) the rest of the run's animal oases were skipped (log 2026-11-02: 6-11 `has animals, but the hero is already away` lines, 4-5 hero sends). User chose option 2: own troop slot and amount for animal oases.
- New village settings (appended last in `VillageSettingEnums`, defaults in `AppDbContext`): `OasisScoutAnimalTroopSlot` (0 = off, default), `OasisScoutAnimalMinTroops` (3), `OasisScoutAnimalMaxTroops` (8), `OasisScoutAnimalMaxPower` (0 = no ceiling). UI: four rows under the Oasis scout box (`VillageSettingTab.xaml/.cs`, `VillageSettingInput`).
- `Tasks/OasisScoutTask.cs`: for an oasis with animals the hero is tried first exactly as before (at home, animals within the hero threshold). If he is away, too weak for these animals, unreadable or the send failed, and an animal troop slot/amount is configured, plain troops are sent (random min-max of the animal slot). `OasisScoutAnimalMaxPower` > 0 skips animals whose estimated power (`AnimalPower`, rough attack table) is above it. With the animal slot at 0 the behaviour is the old one (hero only, same log lines). A failed hero-power read still `break`s the run when no animal troops are configured; with them it only disables the hero for that run.
- Risk to know: `AnimalPower` is a rough ATTACK-power sum, not a combat simulation (animals defend with their own defense values, troops fight with their own attack/defense) - the troop ceiling is a guess to tune from reports; troops can be lost.
- Not changed: no new test (the decision lives in the task; the enum-count tests need nothing, all new keys are in `VillageSettingInput.Get()`).
- Verification: static only (brace/paren balance, XAML well-formed, scopes checked by reading). NOT built, NOT live. Expected log: `sent N troops (slot S) to oasis with animals (X|Y) - estimated animal power P`.

## 2026-11-02 (later) - Queue horizon: each troop keeps its own "not again before" interval again (code, NOT yet built or tested live)

User: "bir ziyarette phalanx yetistiriyorsa, farkli bir zamanda swordsman yetistirmeliydi" - the per-troop intervals (2026-10-27) were ignored once a building had a queue horizon (2026-10-30: "the per-troop interval is not used there"), so slot 1 (Phalanx) was trained at EVERY visit and slot 2 only when Phalanx hit its per-visit cap and the queue was still below the target (log 12:56: Phalanx 50 then Swordsman 10 five seconds later). Decision (option 1): the interval holds inside the horizon flow.
- `Tasks/TrainTroopTask.cs` (`TrainWithHorizon`): a troop whose `TrainTroopSchedule` time (key village, building, troop) is still in the future is skipped (Information line `<Troop> is not due before HH:mm:ss, skipped this visit.`); after a troop was trained its next time is now + its own interval (`GetIntervalSeconds`, 60 s minimum). A troop whose own interval is 0/0 has NO limit in the horizon flow (the hidden general fallback is not applied there, so existing horizon setups keep working). A troop starved of resources gets no interval (it is tried again at the next visit).
- `Commands/Features/Scheduling/QueueHorizonRules.cs`: `HoldUntilAnyTroopDue(next, troopTimes, now)` - when every troop is still waiting, the building's next visit is not planned before the earliest of them (a visit would train nothing). If one troop is due, the planned visit stands.
- Consequences to know: the queue can run below the target (or empty) while all troops wait for their interval - that is the user's rule winning over the horizon. After a restart the schedule is forgotten, so every troop is due once at the first visit (Phalanx and Swordsman together), then they drift apart.
- Tests: `QueueHorizonRulesTest` (+4 `HoldUntilAnyTroopDue` cases).
- Verification: static only. NOT built, NOT live. Expected in the log: Swordsman lines at different visits than Phalanx, `is not due before` lines, next visits no earlier than the earliest troop time.

## 2026-11-02 - Queue horizon: a village that cannot pay is no longer visited every minute; empty Supply runs back off (code, NOT yet built or tested live)

Log 2026-11-02 00:00-10:46 (build with the 10-30/10-31 changes; 0 ERR, 1 login after the 05:58-10:00 sleep gap). Finding: TS village 83873 (A_1202) Barracks, horizon "at least 5 / fill up to 180 min", village short of resources.
309 visits in 6 h (about every 64 s), 41 of them added exactly 1 Phalanx, queue 0-4 min at 296 of 309 visits (target 180). Cause (code + log, consistent): the queue stays below "at least", so `NextVisit` returns "now" and the 60 s floor
(`MinVisitGapSeconds`) is the real interval; the 5-10 min starved retry only ever SHORTENED a visit time (`retry < next`), it never lengthened one. Side effect: each visit asks the hammer villages to supply (`RequestIfNeeded`) - 622 "Supply from hammer" runs, 616 of them "nothing spare to send (reserve protected)", each a Marketplace visit in the single queue (Upgrade building ran 8 times, Oasis scout 7).
- `Commands/Features/Scheduling/QueueHorizonRules.cs`: new `PlanNextVisit(now, fillTo, queue, min, offlineStart, starved, retrySeconds)` -> `VisitPlan(At, Reason)`. Starved (could not pay) and target missed: never earlier than the retry time and never earlier than the regular `NextVisit` (a long queue is not visited sooner because the target was missed); still pulled to the lead point before an offline stretch. Target missed for another reason (per-visit cap): the old rule (earlier of regular and retry). Target reached: regular time.
- `Tasks/TrainTroopTask.cs` (`TrainWithHorizon`): `starved` is set when the page's max amount is below the wanted units or `TrainTroopCommand` returns `MissingResource`. The fallback queue estimate now uses the units the page allowed (closes the TODO "fallback overestimates"). The log line now says why: `Train troop in V: Barracks next visit at HH:mm:ss (<reason>; queue holds N min).` The generic end-of-task line was reworded (it claimed "queue end plus buffer" for horizon buildings).
- `Commands/Features/Scheduling/SupplyBackoff.cs` (new, pure, memory only): after a source reports "nothing spare" for a target it is not asked again for that target for 8-12 min. `SupplyFromHammerTask.RequestIfNeeded` skips a blocked source silently; the "nothing spare" log line now names the time. A restart forgets the backoff (old behaviour).
- Tests: `QueueHorizonRulesTest` (+5 `PlanNextVisit` cases), `SupplyBackoffTest` (new, uses `VillageId`, so it is NOT in the sandbox harness `files.txt`; CI only). `QueueHorizonRulesTest` stays in the harness.
- Not changed (open): whether the hammer villages' own horizon (Workshop 300 min, Stable 120 min) eats what 83873 needs is a guess from the log, not proven. Smithy "no Improve button" repeats every 46-59 min without a stated reason. 10:00 `SwitchVillageCommand` waited 180 s ("villageChanged") after the sleep gap, then re-login worked.
- Verification: static only (brace/paren balance, rule cases checked by hand). NOT built, NOT live. Expected in the next log: starved village `next visit ... (the village cannot pay yet, looking again later; ...)` 5-10 min apart (about 6-10 visits per hour instead of ~50), and `not asked again before HH:mm:ss` replacing most empty Supply runs.

## 2026-10-31 - Map Analysis: "Export CSV" (code, NOT yet built or tested live)

User request: take the Map Analysis result out of the tab. The list was only text lines in a `ListBox` (no copy, no export).
- `Parsers/MapScanCsv.cs` (new, pure): `Build(rows)` writes a CSV (`;` separator, CRLF, distance with the Turkish decimal comma). Columns: Mesafe, Koordinat, Köy, Oyuncu, Kabile, İttifak, Nüfus, Başkent. The coordinate is ONE column written as `x|y` (e.g. `-12|8`), user decision. Name fields (typed by other players) are quoted when they hold `;`, `"` or a line break and get a leading `'` when they start with `= + - @` (Excel formula injection); the generated coordinate column is not touched by that rule.
- `UI/ViewModels/Tabs/MapAnalysisViewModel.cs`: `Search` keeps the structured rows of the last finished search (`_lastRows`, reset when a new search starts); new `ExportCsvCommand` writes `Exports/MapScan_<x>_<y>_<yyyyMMdd_HHmmss>.csv` next to the program (UTF-8 with BOM so Turkish letters open correctly), shows the path in a message box, a failure or an empty search is a message box (not a log line, like the rest of this tab). `FormatRow` uses the shared `MapScanCsv.TribeText`.
- `WPFUI/Views/Tabs/MapAnalysisTab.xaml(.cs)`: "Export CSV" button next to Search.
- Tests: `MainCore.Test/Parsers/MapScanCsvTest.cs` (coordinate format, header and row, Turkish decimal comma, order kept, empty alliance, quoting and formula neutralising). `MapSqlParser.cs`, `MapScanCsv.cs` and the test are in `tools/pure-tests/files.txt`.
- Known limits: still no radius or tribe/alliance/population filter; Map Analysis writes nothing to the log (the tab uses message boxes); the `map.sql` download does not go through the account's proxy.
- Verification: static only (brace/paren balance, XAML well-formed). NOT built, NOT live, tests NOT run.

## 2026-10-31 (analysis, no code) - Live logs 10:13-11:08 reviewed
No code changed by this part; findings went to TODO.md: queue horizon (unreadable queue counted as empty, trickle batches with a 5-10 min retry that starve construction, fallback overestimate, offline cover / Sleep), Auto NPC firing when training lowers crop below `AutoNPCGranaryPercent` (spends gold), `LogSink` not thread-safe (Debug tab exception).

## 2026-10-30 - Training queue horizon: Barracks/Stable are filled ahead of time, offline-hours aware, NPC + hero top-up (code, NOT yet built or tested live)

User request: "kisla ve ahirda surekli asker yetissin, bos kalmasin". Cause found in the code: the bot came back AFTER the queue ended (queue end + 30-60 s buffer, `QueueDeferRules`), so every cycle left the building idle, a longer "not again before" interval left it idle longer, a starved building waited its whole interval, and during offline hours nothing was queued at all.
Design (decided with the user): per building a **queue horizon** (Village Setting, minutes): "at least" = come back when the queue has this much left, "fill up to" = top the queue up to this much at every visit; 0/0 = off = the old behaviour, so nothing changes until it is set.
- `Commands/Features/Scheduling/QueueHorizonRules.cs` (new, pure): `QueueHorizon`, `FillToSeconds` (target, or - within 10 min of an offline stretch - long enough to last until the bot works again, never above the offline cap, never below the target), `NeedSeconds`, `UnitsFor` (round up, per-visit cap = the troop's "amount max"), `NextVisit` (queue end minus "at least"; if that lands in/just before the offline stretch the visit is pulled to 10 min before it so the long fill happens as late as possible; never closer than 60 s), `StarvedRetrySeconds` (5-10 min).
- `Commands/Features/Scheduling/OnlineHoursRules.cs` (new, pure): looks AHEAD in the 24-bit `OnlineHoursMask` (`NextOfflineStart`, `NextOnlineStart`, 48 h scan, midnight-safe). Only the mask is used; the Sleep task is NOT considered (kept by user decision).
- `Commands/Features/TrainTroop/TrainResourceRules.cs` (new, pure): shortage arithmetic, hero availability above the reserve, `UnitsThatFit`, `NpcTarget` (exact per-resource targets that keep the village total and take the deficit from the surplus in proportion).
- `Parsers/DurationText.cs` (new, pure) + `TrainTroopParser.GetUnitSeconds`: per-unit training time from `div.inlineIcon.duration > span.value` ("0:05:47" = 347 s; checked against the real Barracks and Stable captures).
- `TrainTroopCommand.Command` got an optional `Amount` (0 = old random amount; > 0 = the horizon's number, capped to what the page allows, no "low resource" refusal) and `HorizonSettings` (per-building key pairs).
- `Tasks/TrainTroopTask.cs`: a building with the horizon on goes through `TrainWithHorizon` (a busy queue is topped up, not skipped; troops are filled in slot order, each up to its "amount max" per visit; the schedule key is the building as a whole, the per-troop interval is not used there). A shortfall is made up in `MakeUpResources`: village stock -> NPC trade (`TrainUseNpc`, only if the village's own total covers it; `ToNpcResourcePageCommand` + `NpcExchangeToTargetCommand`) -> hero inventory (`TrainUseHeroResource`, `UseHeroResourceCommand`, never below `HeroResourceReserve`); whatever is still unpaid is trained partially. Every failure there is a warning, never Stop/Retry. Stock is read from the page; a value the page cannot give (wood reads -1 on some training pages) falls back to the stored storage.
- New settings (all appended last): Village `<Building>HorizonMin/Target` x5, `TrainUseNpc`, `TrainUseHeroResource`; Account `TrainOfflineCoverMaxMinutes` (default 180, 0 = no offline extension), `HeroResourceReserve` (default 0). UI: Village Setting "Troop training" box (5 range rows + 2 checkboxes), Account Setting "Feature settings" (2 amount rows); validator min <= target.
- Behaviour notes: with a horizon on, "amount max" is a per-visit cap (a big number = no limit; a troop is still active only when its amount is > 0). "Construction first" still stops training while it holds. The hero reserve applies to training only (construction still takes from the hero as before). NPC costs gold.
- Tests: `QueueHorizonRulesTest`, `OnlineHoursRulesTest`, `TrainResourceRulesTest`, `DurationTextTest` (42 cases, all pass in a stripped harness; the full harness cannot download HtmlAgilityPack in this sandbox), `TrainTroopParser.Test` (+2 fixture cases, NOT run here). The two enum-count tests need no change (all new keys are in the input `Get()`).
- Verification: static only (parse check of every touched file, brace balance, XAML well-formed, signatures cross-checked by reading) + the pure rules run. NOT built, NOT live.

## 2026-10-30 (analysis, no code) - Findings from a code review against CLAUDE.md
Rules checked against the code: all 38 Task classes are in the dispatcher switch, AttackCheck is bootstrapped, defensive tasks return no Retry/Stop, `ChromeBrowser` handles `System.TimeoutException`, unscoped village lookups are the 7 listed. New findings went to TODO.md (SendResource positional XPath, `Stop` in secondary tasks, TimerManager timer without try/catch, SleepTask always bootstrapped).

## 2026-10-29 - Hero resource rounding fixed; a stuck "navigating" page no longer retries forever; docs brought up to date (code, NOT yet built or tested live)

Review of the 2026-10-28 22:38-23:59 log (TS-10X101/102, 81 min, 0 ERR, 1 login, no browser close) against the 2026-10-28 evening code snapshot.
- **Hero rounding (`UseHeroResourceCommand`)**: the private `RoundUpTo100` added another 100 to an amount that already was a multiple of 100 (400 -> 500; `HeroReviveTask` had the correct copy), and the rounded amount was compared with the
  inventory before anything else, so 350 in stock and 321 needed asked for 400 and failed. Log: 16,300 resources taken for 14,622 really needed (+11%; the surplus lands in the village and training eats it, the hero stock just drains faster).
  New pure `Commands/Features/UseHeroItem/HeroResourceAmountRules.cs` (`RoundUpTo100` keeps multiples, `AmountToUse(need, inStock)` rounds up but never above the stock while the stock covers the real need, a real shortage stays a shortage).
  `UseHeroResourceCommand` reads the refreshed inventory from `HeroItems` and uses it; the old helper is gone. The amount is typed into the dialog, so a non-multiple of 100 is accepted (the capacity clamp already produced such values).
  Test: `HeroResourceAmountRulesTest`. Rule logic was also checked in a stand-alone script.
- **Transient retry cap**: the 2026-10-28 fix keeps the browser open on "page is navigating" and retries the task after 10 s, with no limit. `TransientStreak` (new, memory only, per account) counts consecutive pre-task transient failures;
  `BrowserErrorRules.MaxConsecutiveTransient` = 6 (about a minute). On the 6th the browser is treated as closed as before 2026-10-28 (`Browser treated as closed: the page was still navigating n times in a row`). The streak is reset
  after any pre-task read that works. Log line now shows `attempt n of 6`. Files: `Services/Browser/TransientStreak.cs`, `BrowserErrorRules.cs`, `Behaviors/AccountTaskBehavior.cs`. Test: `TransientStreakTest`. Both new classes are in `tools/pure-tests/files.txt`.
- **Docs**: CLAUDE.md (2.2 verified live, rule 17 mentions the cap), TODO.md (P0 reduced to the guard test; 10 "verify live" items moved to a separate backlog; stale Recall/XPath items closed; unscoped-lookup list re-counted to 7; new items: attack check missing in log,
  adventure timer/difficulty, hero inventory as build budget, log noise), PROJECT_CONTEXT.md (status and known gaps), README.md (feature list), docs/ONBOARDING.md (Selenium -> Playwright).
- **From the log, for the record**: no `Attack check` line at all (likely the account's checkbox is off, code is wired - TODO); 28.10 `BuildPlanSlotRules` raised no false rejection in 9 plan runs (the real rejection was not triggered); the 10-25 Start adventure fix works live;
  per-troop training worked (13 Phalanx, then 5 capped by resources); `could not read the adventure timer` after a started adventure (fallback worked).
- **CI fix (same day)**: the Test step failed with `TypeInitializationException: ReactiveUI.RxApp ... Destination array was not long enough` - RxApp's static constructor raced between parallel test classes (none of the new tests use ReactiveUI; the extra classes only changed the timing). New `MainCore.Test/TestAssemblyInit.cs` (module initializer) touches RxApp once before any test runs. Unproven until the next CI run; if it still fails the next step is `CollectionBehavior(DisableTestParallelization = true)`.
- Not changed: merchant sends stay multiples of 750 (`MerchantAmountRules`); `System.TimeoutException` in `SyncBridge.Run` (TODO P2).
- Verification: static only (no dotnet in the sandbox). NOT built, NOT live.

## 2026-10-28 (evening) - A build plan for the wrong slot is rejected; Oasis scout knows when the hero is away (code, NOT yet built or tested live)

Log 2026-10-28 14:56-21:16 (account A_1201/1202/1203, 0 ERR, no browser close, no re-login in 6 h 20 min).
- **Wrong building upgraded (A_1202, 17:25):** plan "Brickyard at slot 20 to level 5", but slot 20 holds a Marketplace (level 13). The bot paid 1800 wood /
  600 clay / 1700 iron from the hero inventory and upgraded the Marketplace (the requested cost 1980/1735/2970 is Marketplace level 14; Brickyard 5 is far
  cheaper). Cause: `ValidatePlanCompleteCommand` compared types only when they matched, so a different building fell through to the construct path, and
  `HandleUpgradeCommand` clicked Upgrade on whatever stood in the slot (it logged the real name but never compared it). The same job would have repeated
  at the next run (22:06); the user removed it by hand.
  - `Commands/Features/UpgradeBuilding/BuildPlanSlotRules.cs` (new, pure): a slot is wrong when it holds another real building (not Site / Unknown / level -1)
    and the plan's own building is not under construction there.
  - `ValidatePlanCompleteCommand`: returns `UpgradeBuildingError.SlotOccupied` for that case (new error + `IsSlotOccupied` flag).
  - `GetBuildPlanCommand`: removes that job, logs a Warning (`removed the job "..." - slot N holds X level L, not Y. Nothing was built...`), continues with the next job.
    DESIGN CHOICE: the job is deleted (like a completed plan), not parked; a parked job would block every job behind it.
  - `HandleUpgradeCommand`: safety net, returns Skip without clicking if the slot still holds another building (only reachable if the check above is bypassed).
  - Test: `BuildPlanSlotRulesTest` (CI). The rule class was compiled and run on its own in the sandbox (all cases pass).
- **Oasis scout, hero errors (7 in 6 h):** every one came in a run that had already sent the hero alone to an earlier oasis. The failure line only said "Bot skip this task"
  because the log printed the FIRST error (the generic Skip text) and not the reason added after it. `Tasks/OasisScoutTask.cs`: `DescribeFailure` prints the real reason for
  tile / troop / hero failures; once the hero is sent (or reported "hero not available") the remaining animal oases of that run are skipped with no page visit
  (`heroAway`; about 10 s saved per skipped oasis); empty oases still get troops. A "hero not at home" send is now an Information line, other failures stay Warning.
  Not proven: the old lines did not show the reason, so "hero away" is the explanation that fits all 7 cases, not a read-out.
- **Left as is (user decision):** "construction first" hold stays capped at 3 h (Granary level 20 in A_1201 was not built in 6 h because training ran after the hold expired,
  e.g. 20:00:06 Swordsman, then iron 74 vs 2840 needed).
- **Observed, not changed:** two stalls with no log lines and tasks overdue: 18:25:49 -> 20:00:00 (94 min; due from 19:02) and 20:55 -> 21:16 (9 min late). No pause, no error.
  Likely the computer or the app was suspended; the log cannot prove it.
- Verification: static (brace/paren balance) + the pure rule compiled and run alone. NOT built, NOT live.

## 2026-10-28 - A page that is still navigating no longer closes a healthy browser (code, NOT yet built or tested live)

Log 2026-10-28 10:33-12:09 (TS-10X101/102, build with the 10-27 evening changes): 5x "Browser was closed mid-task" (10:51:03, 11:07:21,
11:21:21, 11:53:18, 11:59:55), each followed by a re-open and a full re-login (5 of the 8 logins). All five: Upgrade building starts,
`DismissCookieConsentCommand` runs, the task ends with `BrowserClosed` one second later. Cause (from code; the closing lines carry no
exception text, so consistent with the log but not proven): `AccountTaskBehavior` caught ANY `BrowserException` and closed the browser, and
`browser.Html` (`PlaywrightDriver.PageSource` -> `ContentAsync`) throws one whenever a navigation is in flight ("Unable to retrieve content
because the page is navigating..."; the same text was logged 5x as ERR from `UpdateStorageCommand`/`UpdateBuildingCommand`). Likely also the
answer to TODO P1 "why does the browser close" and the missed Recall window of 2026-10-23.
- `Services/Browser/BrowserErrorRules.cs` (new, pure): `IsTransient(message)` - navigation-in-flight markers; "gone" markers win over them.
- `Services/Browser/PlaywrightAdapters.cs`: `SyncBridge.RunRetryingTransient` (6 attempts x 500 ms) used by `PageSource`; other failures unchanged.
- `Behaviors/AccountTaskBehavior.cs`: now takes `ILogger`. Before the task: a transient `BrowserException` that outlasts the retries returns
  `Skip` with `ExecuteAt` = now + 10 s (browser stays open; rule 16). After the task: a transient one keeps the task's own result. Any other
  `BrowserException` still closes the browser, but the message is now logged (`Browser treated as closed: ...`).
- `Commands/Misc/DismissCookieConsentCommand.cs`: a transient script failure ("Execution context was destroyed") is no longer a failure
  ("Bot must retry" 2x in the log); closed browser / driver-not-ready still fail.
- Tests: `BrowserErrorRulesTest` (+ `tools/pure-tests/files.txt`). The rule class was compiled and checked on its own in the sandbox (all cases
  pass); the xUnit file and the harness could not run here (HtmlAgilityPack download blocked).
- Not changed (from the same log, still open): Upgrade building polls every 1-7 min while "Construction queue is full" (53 runs, 70 WRN);
  Oasis scout hero send failed at 11:44:55 with no reason in the log; adventure difficulty prints `unknown`.
- Verification: static + the pure rule check. NOT built, NOT live.

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
