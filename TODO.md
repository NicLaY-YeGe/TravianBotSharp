# TODO.md — TravianBotSharp

Bu dosya proje üzerinde açık/bekleyen işleri takip eder. Format: `- [ ] Açıklama (kaynak/not)`.

## SwitchTab / Click Zaman Aşımı / Kararlı XPath (2026-10-21) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Değişiklik:** `SwitchTab` bekleme koşulu sekme çubuğu olmayan geçici sayfada patlamıyor; `ChromeBrowser` `System.TimeoutException` yakalıyor (Click → Force denemesi, diğerleri → `Retry`); element yolları `StableXPath()` ile id'ye çıpalı. Ayrıntı: CHANGELOG.md 2026-10-21.
- [ ] **Kullanıcı: derle + `dotnet test`** (`ChromeBrowser.cs`, `SwitchTabCommand.cs`, `SwitchVillageCommand.cs`, `RecallTroopCommand.cs`, `SendReinforcementCommand.cs`, `DemolishCommand.cs` gerçek derlemeden geçmedi; parser ve XPath testleri harness'ta geçti: 194/0). Ayrıca `packages.lock.json` yenilenmeli (`dotnet restore`).
- [ ] **Canlı doğrulama:** Oasis scout log'unda `SwitchTabCommand {"TabIndex":"2"}` sonrası `ArgumentOutOfRange` çıkmamalı; görev `SendTroopsCommand`'a ilerlemeli. Sekme geçişi kullanan diğer görevler (Farm list, Send resource, NPC, Settle) de bir kez izlenmeli.
- [ ] **Canlı doğrulama:** `Click timed out (...) - retrying without the actionability checks` uyarısı çıkarsa Force denemesinin sonucuna bak; `Click timed out, the element is probably no longer on the page` Retry'ı tekrarlıyorsa `Screenshots` klasöründeki PNG'ye bakıp sayfada ne olduğunu doğrula (kırılgan XPath hipotezi doğrulanmadı).
- [ ] **Açık:** `SendResourceCommand`'ın kendi `node.XPath` dizgileri `StableXPath`'e geçirilmedi.
- [ ] **Açık:** `PlaywrightElement.Text/GetAttribute` elementin kaybolduğu durumda 30 sn sonra ham `TimeoutException` atabilir (SyncBridge yalnızca `PlaywrightException` çeviriyor); `BrowserException` ile eşlemek tarayıcıyı kapatma yan etkisi yaratır, ayrı karar gerektirir.
- [ ] **Açık:** `Settle.ToMapCommand` 5–11 sn sürüyor — `WaitUntilState.Load` bekleyişi incelenmeli.
- [ ] **Not:** Bu ağaç `recall-fix-patch.zip`'in `DefendDonateTask.cs`'ini ve `AttackReservation*` / `DefendAttackMemory` / `DefendResourceTargetRules` dosyalarını İÇERMİYOR. Aşağıdaki Defend/rezerv maddeleri o ağaç için; ana ağaçla birleştirirken `DefendDonateTask.cs` patch sürümü kullanılmalı.

## Recall Koordinat Eşleşmesi + Defend Tanılama (2026-10-20, ikinci) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Değişiklik:** Recall artık gizli yön karakterli / U+2212 eksili koordinat metnini eşleştiriyor; bulamazsa log'a sayfadaki hareket başlıklarını yazıyor. Defend gönderi planından önce stok/tüccar/kapasite satırı logluyor. Ayrıntı: CHANGELOG.md 2026-10-20 (ikinci).
- [ ] **Kullanıcı: derle + `dotnet test`** (`DefendDonateTask.cs`, `RecallTroopCommand.cs` gerçek derlemeden geçmedi; parser ve saf kurallar harness'ta geçti).
- [ ] **Canlı doğrulama:** Sonraki dodge'tan ~50 sn sonra Recall `Could not find a cancel button` yerine butona tıklamalı; askerler geri dönmeli.
- [ ] **Canlı doğrulama:** Defend log'unda `Defend send plan for …: stock … free merchants N x capacity C = M; plan …` satırını oku; `crop=8` benzeri bir kısıtlama olursa satır sebebi gösterir (stok mü, tüccar mı, kapasite mi). Gerekirse ona göre düzelt.
- [ ] **Kullanıcı kontrolü:** 12:37'de (35|-5)'e giden dodge askerleri (varış 12:46:40) geri çağrılamadı; dönüp dönmediğine Rally Point'ten bak.

## Öncelikli Görevler Öne Geçer — Recall Penceresi Düzeltmesi (2026-10-20) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Değişiklik:** Vakti gelen Dodge/Defend/Recall/Login/WakeUp, diğer vakti gelmiş görevlerin önüne geçer (rezervden bağımsız). Sebep ve ayrıntı: CHANGELOG.md 2026-10-20.
- [ ] **Kullanıcı: derle + `dotnet test`** (`TimerManager.cs` gerçek derlemeden geçmedi; saf kurallar harness'ta geçti).
- [ ] **Canlı doğrulama:** Dodge'tan sonra `Recall dodge troops` planlanan zamanda (gönderim + ~50 sn) başlamalı ve `Could not find a cancel button` çıkmamalı.
- [ ] **Kullanıcı kontrolü:** TS-3202 (ve diğer köyler) için `Defend on attack` kutusu açık mı? 2026-10-20 saldırısında Defend hiç çalışmadı.
- [ ] **Kullanıcı: elle kontrol:** 11:29:46'da (35|-5)'e giden dodge askerleri geri çağrılamadı (varış 11:39:18); Rally Point'ten durumuna bak.

## Saldırı Rezervi: İniş −3/5 dk → +15 sn Dodge'a Ayrıldı (2026-10-19, üçüncü) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Değişiklik:** Saldırının iniş zamanı okununca (Dodge/Defend) rastgele 180–300 sn önceden iniş+15 sn'ye kadar yalnızca Dodge/Defend/Recall/Login/WakeUp (ve `BypassOnlineHours` gönderimler) çalışır; diğer görevler kuyrukta bekler, saldırıdan sonra devam eder. Ayrıntı: CHANGELOG.md 2026-10-19 (üçüncü).
- [ ] **Kullanıcı: derle + `dotnet test`** (`TimerManager.cs`, `AttackWakeWindow.cs`, `DodgeTroopTask.cs`, `DefendDonateTask.cs` gerçek derlemeden geçmedi; saf kurallar harness'ta geçti).
- [ ] **Canlı doğrulama:** Saldırıda log'da `other tasks wait from … until …` satırı; o aralıkta Overflow/Supply/Upgrade/Oasis scout BAŞLAMAMALI; Dodge `Dodged … will recall` ve recall zamanında çalışmalı; `until` geçince bekleyen görevler devam etmeli.
- [ ] **Açık (istenirse):** 180–300 sn'yi ve +15 sn'yi hesap ayarına bağla.
- [ ] **Açık (istenirse):** Rezervin başlamasına kısa süre kala uzun sürebilecek görevleri (bekleme/uyku) hiç başlatma.

## Defend Yeniden Tasarlandı — Phalanx Eğit + Kaynağı Başka Köye Gönder (2026-10-19, ikinci) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Değişiklik:** Defend artık saldırı görülür görülmez, saldırı başına bir kez: T1 eğit → kalan tüm kaynağı (Sığınak yok sayılır) başka köye gönder; ittifak bağışı çıktı. Hedef: deposu müsait köy öncelikli, rastgele, saldırı altındakiler elenir. Pazarcı yoksa atlar. Süre bütçesi Dodge'dan önce biter. Ayrıntı: CHANGELOG.md 2026-10-19 (ikinci).
- [ ] **Kullanıcı: derle + `dotnet test`** (`DefendDonateTask.cs` gerçek derlemeden geçmedi; yeni testler saf harness'ta geçti).
- [ ] **Canlı doğrulama:** Saldırıda log sırası: `defending now (budget …s, once for this attack)` → eğitim → `Sending resources from village … to …` → `Defend done …`; ardından Dodge'un `Dodged … will recall` satırı. Defend ikinci kez başlamamalı.
- [ ] **Canlı doğrulama:** Bütçe iptali — takılma olursa `Defend ran out of its …s budget` satırı görünmeli ve Dodge zamanında çalışmalı.
- [ ] **Kullanıcı ayarı:** Dodge "send before impact" 30 sn → 60–90 sn (araya başka görev girerse). Account Setting'de Tribe seçili olmalı. Defend+Donate kutusu açık olmalı.
- [ ] **Açık:** Dodge önceliği — Defend'den önce sırada bekleyen görevler (Overflow/Supply/Upgrade/Oasis scout) saldırı yakınken kuyruğa girmemeli ya da Dodge'un önüne geçmemeli (ayrı iş).
- [ ] **Açık (istenirse):** Tüm tahıl gönderilince köyde asker beslemesi eksiye düşerse tahıldan bir tampon bırak.

## Defend Cranny'de Takılıp Dodge'u Kaçırtıyordu — Düzeltme Bekliyor Derleme/Canlı Doğrulama (2026-10-19)

- [x] **Bugfix:** `GetCrannyProtectionCommand` ve `TrainCheapestTroopCommand` binaya gitmeden önce `ToDorfCommand(2)` çağırıyor (Rally Point sayfasında bina slotu yok → 180 sn takılma). Ayrıntı: CHANGELOG.md 2026-10-19
- [x] **Bugfix:** `TrainCheapestTroopCommand` tribe'ı `AccountSettingEnums.Tribe`'dan okuyor (`AccountsInfo.Tribe` hep `Any` idi → hiç asker eğitilmiyordu).
- [x] **Bugfix:** `DefendDonateTask` Cranny/eğitim/bağış hatalarında `Skip` (Retry kuyruğu tutup Dodge'u geciktiriyordu); Dodge açıksa bitiş hedefi `max(60, DodgeSendSecondsBeforeImpact + 30)` sn öncesi.
- [ ] **Kullanıcı: derle + `dotnet test`** (`GetCrannyProtectionCommand.cs`, `TrainCheapestTroopCommand.cs`, `DefendDonateTask.cs`).
- [ ] **Kullanıcı ayarı:** Dodge "send before impact" 30 sn → 60–90 sn'ye çıkar (araya başka görev girerse 30 sn yetmeyebilir). Account Setting'de Tribe seçili olmalı.
- [ ] **Canlı doğrulama:** Bir saldırıda log'da Defend: `GetCrannyProtectionCommand` → Barracks eğitimi → bağış, ardından Dodge'un `Dodged … will recall` satırı görülmeli; Defend 180 sn takılmamalı.
- [ ] **Açık (istenirse):** Dodge/Defend yolunda `GetElement` bekleme süresini kısalt (3 dk → ~10 sn), böyle bir takılma yine olsa kuyruğu uzun tutmasın.

## Dodge/Defend Gelen Saldırıyı Görmüyordu — Düzeltme Bekliyor Derleme/Canlı Doğrulama (2026-10-18, ikinci)

- [x] **Bugfix:** `RallyPointOverviewParser.GetIncomingAttackSeconds` sayacı `value` özniteliğinden okuyor (`data-value` fallback). Eskiden her okuma `null` → Dodge/Defend `Skip` → görev siliniyordu. Gerçek Rally Point capture'ı ile doğrulandı. Ayrıntı: CHANGELOG.md 2026-10-18 (ikinci).
- [ ] **Kullanıcı: derle + `dotnet test`** (`RallyPointOverviewParser.Test.cs` yeni; fixture `MainCore.Test/Parsers/RallyPointOverview/IncomingAttack.html`).
- [ ] **Canlı doğrulama:** Saldırı yoldayken log'da sırayla `Incoming attack on … lands in Ns - will send dodge troops in …s` → `Dodged … will recall in …s` → recall görülmeli. Defend için `will train+donate in …s` ve sonrası.
- [ ] **Kullanıcı ayar kontrolü:** Dodge kutusu açık, en az bir asker tipi seçili, hedef X|Y girili ve kayıtlı; Defend+Donate kutusu varsayılan KAPALI (açılmalı).
- [ ] **Açık (istenirse):** Okuma başarısız olursa görevi `Skip` ile silmek yerine birkaç sn sonra yeniden dene (aynı dalga için tek şans sorunu; CLAUDE.md §2l).
- [ ] **Açık (istenirse):** Gönderen köy kullanıcının kendi hesabına aitse (capture'da TS-3202 → TS-3201) Dodge/Defend tetiklenmesin.
- [ ] **Açık:** Defend'in Cranny okuma / en ucuz T1 eğitimi / ittifak bağışı adımları canlı saldırıyla hâlâ doğrulanmadı.

## Login Ekranında Takılma — Düzeltme Bekliyor Derleme/Canlı Doğrulama (2026-10-18)

- [x] `IsLoginPage` gevşetildi (şifre input'u / `#loginScene`), `AccountTaskBehavior` tek Refresh + teşhis mesajı, `LoginCommand` buton fallback'i, ham TS32 login fixture'ı + test. Ayrıntı: CHANGELOG.md 2026-10-18.
- [ ] **Kullanıcı: derle + `dotnet test`** (`LoginParser`, `AccountTaskBehavior`, `LoginCommand` gerçek derlemeden geçmedi).
- [ ] **Canlı doğrulama:** Aynı durum tekrarlarsa log'daki yeni satırı (`[url=…, title=…, body class=…]`) gönder — formun neden çizilmediğini asıl o gösterecek.
- [ ] **Açık (istenirse):** Aynı hata üst üste N kez olunca Telegram'a "bot oyuna giremiyor" bildirimi / hesabı duraklatma.

## Chrome Otomatik Güncelleme Kapatıldı — İzleme (2026-10-14, ikinci) — Operasyonel, Kod Yok

- [x] TS32 sunucusunda Chrome güncellemesi politika + servis + görev düzeyinde kapatıldı. Ayrıntı ve geri alma: CHANGELOG.md 2026-10-14 (ikinci).
- [ ] **Kullanıcı:** `chrome://version` ile Chrome ana sürümünü not et, **TBS'yi yeniden başlat** (chromedriver açılışta eşleşir), `chrome://settings/help`'te güncellemenin engellendiği mesajını doğrula.
- [ ] **Kullanıcı:** Birkaç gün log'u izle. `Timed out after 180 seconds` sürerse Chrome güncellemesi kök neden DEĞİLDİR; o zaman `Send form failed at step '…'` log satırını gönder (asıl neden: hangi kaynak kutusu/XPath).
- [ ] **Açık:** `chrome://policy` hâlâ `UpdatePolicy=1` gösteriyor (kayıt defterinde karşılığı yok) — gerçek durumun Chrome sürümünün sabit kalmasından teyidi gerek.
- [ ] **Açık (istenirse):** Chrome for Testing desteği — `ChromeBrowser.Setup`'ta `options.BinaryLocation` + `ChromeDriverInstaller.GetChromeVersion`'ın sürümü o yoldan okuması (yol yoksa mevcut davranış korunur). Sabit sürüm istenirse kodlanır.
- [ ] **Açık:** Güncelleyici kendini yenilerse servis/görev adı değişebilir (sürüm numarası adın parçası) — Chrome sürümü beklenmedik şekilde değişirse tekrar kontrol et.

## Tüccar Formu Hataları Skip + Kısa Bekleme (2026-10-14) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Bugfix:** `SendResourceCommand` form hataları `Skip`, bekleme 3 dk → 8 sn, ayrıntılı hata logu; 6 çağıran görev sonucu `Stop` ile sarmıyor. Ayrıntı: CHANGELOG.md 2026-10-14.
- [ ] **Kullanıcı: derle**, sonra canlıda doğrula: `Balance resources`/`Overflow to hammer` bir transferde takılırsa hesap DURMAMALI; log'da `Send form failed at step '…'` satırı (adım, kaynak=miktar, XPath, plan) görünmeli. O satırı bana gönder — TS-3203'teki asıl nedeni (hangi kaynak kutusu) kesinleştirir.
- [ ] **Açık:** Kesin kök neden bilinmiyor (log hangi kutuyu yazmıyordu). Hata kalıcıysa Travian formunda belirli bir kaynak kutusunun gizli/devre dışı render edilme koşulu (ör. o kaynaktan hiç gönderilebilir yok) incelenmeli.
- [ ] **Açık:** `Skip` görevi kuyruktan düşürür (§2l); `Balance resources` bir hatadan sonra yeniden kuyruğa giriyor mu log'dan doğrula. Girmiyorsa `NextExecute…` ile yeniden zamanlama eklenmeli.
- [ ] **Açık (ayrı):** Çevrimdışı saat sonrası 05:03 oturum düşmesi (`Account is logout`) — yeniden giriş çalıştı; tekrarlarsa uyku/offline pencere akışı (§2n) ile birlikte bakılmalı.

## Marketplace Seviye 0 → MissingBuilding (2026-10-13) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Bugfix:** `ToBuildingByTypeCommand` yalnızca Level>0 binayı kabul ediyor. Ayrıntı: CHANGELOG.md 2026-10-13.
- [ ] **Kullanıcı: derle**, TS-3204'te canlı doğrula (log'da "No marketplace in … cannot send overflow to hammer" uyarısı, hesap durmamalı). Ayrıca: BAŞKA bir "To…Page" komutunun seviye-0 bina ile bilinçli çalışması gerekiyorsa (yükseltme akışı `ToBuildingByLocation` kullanıyor, etkilenmez) bildir.
- [ ] **Açık:** Pazarı olmayan köyde Overflow to hammer boşuna yeniden kuyruğa giriyor; istenirse köyde Marketplace yoksa görevi hiç ekleme.

## Sync Attack Hero Seçeneği (2026-10-11) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Özellik:** Sync Attack köy satırında "Send hero" (tek köy). Ayrıntı: CHANGELOG.md 2026-10-11.
- [ ] **Kullanıcı: derle**, sonra canlıda küçük bir Reinforcement ile dene; probe log'unda yolculuk süresi hero'suz süreden farklı (hero hızı) görünmeli.
- [ ] **Açık:** Hero gönderim anında macerada/başka hareketteyse gönderim `Retry` ile düşer; gece baskını için hero'nun o saatte evde olması gerekir (istenirse hero eve dönmemişse ayrı bir bekleme/uyarı eklenebilir).

## Tüccar Gönderimi: Tıklama Sağlamlaştırma (2026-10-09, ikinci) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [ ] **TS32 "Merchant count did not drop" (2026-10-09):** Stop yerine uyarıya çevrildi. Bot ekran görüntüsü gönderimin yapılmadığını gösterdi (tıklama kayıt olmadı); scroll + JS-click fallback eklendi. Canlıda log'da 'Send click did not register - retrying with a script click' görülürse fallback devreye girmiş demektir; ayrıca tekrar ederse log'daki `Could not confirm the shipment` satırının zamanında Marketplace'e bakıp gönderimin gidip gitmediğini kontrol et. Ayrıca Supply from hammer 100'lük minik yüklerle sık gönderiyor (yuvarlama) — istenirse minimum gönderim eşiği eklenebilir.

## Depo/Ambar Kapasitesi → Otomatik Büyütme (2026-10-09) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Bugfix:** `StorageLimit` (bina maliyeti > depo/ambar kapasitesi) artık botu durdurmuyor; Warehouse/Granary yükseltmesi kuyruğun başına ekleniyor. Ayrıntı: CHANGELOG.md 2026-10-09.
- [ ] **Kullanıcı: derle + `dotnet test`**, sonra TS12 A_1202'de canlı doğrula: log'da "...is too small for Barracks level 10, queued a Warehouse upgrade first." ve ardından Warehouse inşaatı görünmeli; depo yükselince Barracks 10 devam etmeli.
- [ ] **Açık:** Birden çok depo varsa en yüksek seviyeli olan +1 yapılıyor (en ucuz olan değil) — istenirse "en düşük seviyeli depo" tercih edilebilir. `NpcResourceCommand`/`NPCTask`'taki `StorageLimit` kullanımı değişmedi.

## Gelen Saldırıda Uyanma Penceresi (2026-10-03, ikinci) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Özellik:** Saldırı bot uyanıkken tespit edilir ve uyku/offline saatte varırsa, Dodge/Defend+Donate/Recall için 5-10 dk önce uyanma penceresi. Account Setting'de açma kutusu (varsayılan AÇIK) + önce/sonra dakika aralıkları. Ayrıntı: CHANGELOG.md 2026-10-03 (ikinci).
- [ ] **Kullanıcı: derle + `dotnet test`** (`DodgeTroopTask`, `DefendDonateTask`, `RecallTroopTask`, `AttackWakeWindow`, ayar UI'ı gerçek derlemeden geçmedi).
- [ ] **Canlı doğrulama:** En kolay yol — Dodge/Defend ayarı açık bir köyde, hesabı offline saat aralığına yaklaşırken (veya uykuya girmeden hemen önce) gelen/simüle edilen bir saldırıyla dene. Log'da sırayla "Wake window for the incoming attack…" → (uykudaysa) "waking up early" → "Awake and logged in…" → "Dodged … will recall" → recall → pencere sonunda uyku görülmeli.
- [ ] **Doğrulanacak (mevcut davranış):** Aynı köye ARDIŞIK ikinci saldırı dalgasında Dodge/Defend görevi yeniden ekleniyor mu? (`UpdateStorageCommand`/`UpdateVillageListCommand` yalnızca `IsUnderAttack` yok→var geçişinde ekliyor.)
- [ ] **İstenirse sonra:** "nöbetçi" kontrol (uyku boyunca arada bir kısa uyanıp saldırı bakma) — kullanıcı şimdilik GEREK YOK dedi.

## Sync Attack "Uyanma Penceresi" — Gece Baskını (2026-10-03) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Özellik:** Zamanlı gönderim uyku/offline saate denk gelse bile bot çıkıştan 5-10 dk önce uyanıp göndersin, 5-10 dk sonra tekrar uyusun (Sync Attack sekmesinde kutu). Ayrıntı: CHANGELOG.md 2026-10-03.
- [ ] **Kullanıcı: derle + `dotnet test`.** `TimerManager`, `SleepCommand`, `NextExecuteSleepTaskCommand`, `WakeUpTask` ve ViewModel/XAML gerçek derlemeden geçmedi (yalnızca `csc` sözdizimi taraması).
- [ ] **Canlı doğrulama (en riskli kısım):** Gönderim saatini offline saate ve ayrı bir denemede uyku (SleepTask) sırasına denk getirerek dene. Log'da sırayla görülmeli: "waking up early" (uykudaysa) → "Awake and logged in…" / "session expired, logging in ahead of time" → gönderim → pencere sonunda bir sonraki uyku. Önce küçük bir Reinforcement ile dene.
- [ ] **Wave Attack'a da pencere eklensin mi?** (şimdilik yalnızca Sync Attack; `WaveAttackPlanTask` aynı `SendTroopsAtTimeTask`'ı kullanıyor, yani ek iş küçük: ViewModel + XAML + plan DTO.)
- [ ] **Özellik 2 (henüz başlanmadı — kullanıcı istedi):** Casus raporu okununca, sabit bir köyden, kullanıcının yazdığı sabit asker adetleriyle (mevcut marj/veri yaşı ayarlarıyla karşılaştırıp) otomatik gönderim. Tasarım: Oasis Scout kalıbında VİLLAGE ayarları; güvenlik sınırları (aynı hedefe tekrar gönderme bekleme süresi + günlük üst sınır, kolon eklemek gerekir → idempotent ALTER TABLE) şart. Bağımlılık: casus raporundan dolu garnizon okumanın canlıda hiç doğrulanmamış olması.

## "Claim Quest" Sonsuz Döngü Düzeltmesi (2026-09-29) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Bugfix (kullanıcı ekran görüntüsüyle bildirdi):** `ClaimQuestCommand` her zaman yalnızca görev ustasının 1. sekmesinin 1. sayfasına bakıyordu; "yeni görev" balonu ise HER sekimde/sayfada bir şey varsa yanıyor. Toplanacak görev 2. sekmede ("General tasks") ya da 1. sekmenin 2./3. sayfasında duruyorsa balon hiç sönmüyor, `UpdateQuestCommand` görevi her sayfa geçişinde yeniden sıraya ekliyor, bot "Claim quest" görevini her ~5-8 saniyede bir sonsuza dek tekrarlıyordu. Artık HER sekim + HER sayfa dolaşılıyor (en fazla 3 tam tur, hâlâ çözülmezse döngüye girmek yerine uyarı logu + `Ok`). Yeni parser metodu: `QuestParser.GetNextPageButton`. Ayrıntı: CHANGELOG.md 2026-09-29, PROJECT_CONTEXT.md §5zn.
- [ ] **Kullanıcı: derle + canlı gözlem.** Log'da artık "Claim quest" görevinin sürekli tekrarlanmadığını, gerçekten toplanacak bir şey kalmayınca durduğunu doğrula. Eğer birden fazla sekme/sayfa gerçekten devreye girdiyse log'da `SwitchTabCommand {"TabIndex":"1"}` (2. sekme, 0-index) ve/veya sayfalama tıklamaları görünmeli.
- [ ] **Pure-test'te ilk kez `QuestParser` var ama `ClaimQuestCommand`'ın kendisi (Immediate.Handlers gerektirdiği için) hiç gerçek derlemeden geçmedi** — yalnızca `csc -target:library` sözdizimi kontrolü yapıldı.

## Defend + Donate — Başka Oturumdan Gelen Patch Birleştirildi (2026-09-27, ikinci/üçüncü) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Özellik (kullanıcının başka bir oturumda ürettirdiği `defend-donate-2026-09-27.zip`, bu oturuma eklendi):** Köye saldırı tespit edilince (`RallyPointOverviewParser.GetIncomingAttackSeconds`, varışa kadar kalan süre) sığınak (Cranny) koruması üstündeki kaynakla en ucuz T1 birliği eğitiyor, kalan kaynağı ittifak Recruitment bonusuna bağışlıyor — hedef, varışa ~60 sn kala bitmiş olmak. Yeni `DefendDonateTask` + `TrainCheapestTroopCommand`/`DonateAllianceBonusCommand`/`GetCrannyProtectionCommand`/`ToAllianceBonusPageCommand` + `CrannyParser`/`AllianceBonusParser`. Köy ayarında "Defend + donate on attack" checkbox'ı (`VillageSettingEnums.DefendDonateEnable`, varsayılan KAPALI). Ayrıntı: CHANGELOG.md 2026-09-27 (ikinci), PROJECT_CONTEXT.md §5zl.
- [x] **Birleştirme çakışması elle çözüldü:** Patch'in kendi `AppDbContext.cs`'i bu oturumun savaş-karar-motoru değişikliklerini (ScoutedTargetGarrisons tablosu vb.) içermiyordu — o dosya kopyalanmadı, yalnızca patch'in tek DB eklemesi (`DefendDonateEnable` varsayılanı) elle eklendi.
- [x] **Bugfix (2026-09-27, üçüncü, kullanıcının paylaştığı CI hata logu sayesinde bulundu):** `DefendDonateTask.cs`'de `ToRallyPointOverviewCommand` için eksik `using MainCore.Commands.Features.DodgeTroop;` — gerçek `dotnet build` 3 yerde `CS0246` verdi (bu oturumun kendi `csc` taraması yalnızca ÇAKIŞAN dosyaları kapsadığı için kaçırılmıştı). Düzeltildi + patch'in TÜM dosyaları bu vesileyle elle satır satır incelendi. Ayrıntı: CHANGELOG.md 2026-09-27 (üçüncü).
- [ ] **Kullanıcı: derle + `dotnet test`.** Bu patch'in dosyaları pure-test harness kapsamında DEĞİL (hepsi EF Core/Selenium/Immediate.Handlers'a bağımlı) — yalnızca `csc` sözdizimi kontrolünden geçti.
- [ ] **Canlı doğrulama — patch'in kendi notunda da vurgulanan en riskli kısım:** Bağış butonunun JS olayları (`keyup` tetiklemesi ile `checkButtonState`'in gerçekten aktifleşmesi) canlıda hiç denenmedi. Önce tek köyde, düşük riskli/gerçek bir saldırıyla test edilmeli.
- [ ] **Canlı gözlem:** Saldırı tespit edilince log'da doğru sırayla (Cranny kontrolü → en ucuz T1 eğitim → ittifak bağışı) çalıştığı, varıştan ~60 sn önce bittiği, checkbox kapalıyken görevin hiç tetiklenmediği doğrulanmalı.



- [ ] **TS32, giriş sayfası 180s timeout:** Kullanıcı "gece modu TS32'de aktif kalmış olabilir" dedi, kendisi kapatacak. Selector/iframe/kopya-element ihtimalleri incelemede tek tek elendi (bkz. CHANGELOG.md 2026-09-26); botun ekran görüntüsü kullanıcının manuel gördüğünden farklı bir sayfa şablonu (tam site iskeleti + "Log in with mobile version" checkbox'ı işaretli) gösteriyordu. **Tekrar olursa:** botun kendi ekran görüntüsünü tekrar iste, gece modu kapatıldıktan sonra da oluyor mu diye sor.
- [ ] **TS12 (A_1202), hero-inventory `InventoryParser.GetItemSlot` 180s timeout:** Kullanıcı "ciddi değil, not edelim" dedi. En olası neden: aranan eşya türü envanterde tükenmiş olmasına rağmen `GetElement`'in bekleme döngüsü bunu erken tespit edip çıkmıyor (bkz. CHANGELOG.md 2026-09-26). **Tekrar olursa:** o anki gerçek kahraman envanterini (eşya var mı yok mu) doğrudan kontrol et.

## Çok Tarlalı (Crop) Oasis Tarayıcı (2026-09-25) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Özellik:** Village Setting sekmesinde girilen merkez koordinat + max mesafeden itibaren, en yakından başlayarak tek tek harita karesi açıp "Abandoned valley" oasislerin Croplands sayısını okuyan, eşiği geçenleri CSV'ye anında ekleyen, kareler arası rastgele (`CropScanGapMin/MaxSeconds`) bekleyen TEK SEFERLİK bir görev (`CropOasisScanTask`). Ayrıntı: CHANGELOG.md 2026-09-25.
- [x] **Bugfix (2026-09-25, ikinci):** İlk teslimattan sonra GitHub Actions build'i `CustomServiceScopeFactory.cs`'de `CropOasisScanTask.Task` için çift (birebir kopya) `case` bloğu yüzünden 4 hatayla kırıldı — kopya blok silindi. Ayrıntı: CHANGELOG.md 2026-09-25 (ikinci). Kullanıcının bir sonraki Actions çalıştırmasında doğrulaması gerekiyor.
- [x] **Canlı bugfix (2026-09-25, üçüncü):** Kullanıcı "sadece tek koordinat taramış" bildirdi — canlı log incelemesi, hesap "Restart" butonunun `TaskManager.Clear()` çağırıp görevi kalıcı olarak sildiğini, `CropOasisScanTask`'ın `RxQueue.AccountInitHandler`'a hiç eklenmediği için bir daha asla geri gelmediğini ortaya çıkardı. Görev artık `trapTask`/`oasisScoutTask` ile aynı koşullu-ekleme deseniyle bootstrap listesine eklendi (Restart/relogin sonrası tarama baştan yeniden başlıyor, kaldığı yerden değil). Ayrıntı: CHANGELOG.md 2026-09-25 (üçüncü).
- [ ] **Kullanıcı: bir sonraki build + canlı gözlemde doğrula.** Özellikle: (1) Restart sırasında/sonrasında tarama gerçekten yeniden tetikleniyor mu, (2) baştan başladığında yeni bir CSV dosyası mı açıyor yoksa eskisine mi devam ediyor (tasarım gereği YENİ dosya açması bekleniyor — dosya adı zaman damgalı), (3) art arda birden fazla Restart'ta görev tekrar tekrar kuyruğa binmiyor (`IsExist` kontrolü bunu engellemeli).
- [ ] **Ertelenmiş, ele alınmayan iyileştirme:** Kalan koordinat kuyruğunu DB'ye yazıp gerçekten kaldığı yerden devam etmeyi sağlamak (şu anki çözüm sadece "yeniden başlat" — Restart/relogin sonrası daha önce taranmış kareler tekrar taranıyor, sonuç kaybı yok ama zaman kaybı var).
- [ ] **Kullanıcı: derle + `dotnet test` + XAML'in gerçekten göründüğünü kontrol et.** Parser + koordinat kuralları pure-test harness'ta 104/104 (yeni 10 test dahil) ama `Task`/dispatcher/`SaveVillageSettingCommand`/`VillageSettingInput`/XAML tarafı yalnızca sözdizimi düzeyinde csc ile kontrol edildi, EF Core+WPF+ReactiveUI'ye bağımlı olduğu için bu ortamda gerçek derleme YAPILAMADI — şu ana kadarki en riskli teslimatlardan biri.
- [ ] **Village Setting sekmesi:** Yeni "Crop oasis scan" bölümü görünüyor mu (checkbox + Center X/Y + Max distance + Minimum Croplands + Wait between tiles), değerler kayıt/yükleme arasında doğru gidip geliyor mu kontrol edilmeli.
- [ ] **Canlı gözlem (küçük bir mesafeyle başla, ör. max distance=3):** Checkbox işaretlenip Save'e basılınca görev kuyruğa giriyor mu; log'da "Crop scan: starting from..." ve her karede "Crop scan: checked (X|Y), N tiles left." görünüyor mu; `CropScans/` klasöründe (uygulamanın çalıştığı klasörün altında) bir CSV dosyası oluşup eşleşen satırlar canlı ekleniyor mu; tarama bitince log'da "finished" mesajı ve checkbox'ın kendiliğinden işaretsiz hale geldiği (Save'e tekrar basılmadan, sadece DB'de) doğrulanmalı.
- [ ] **Kareler arası bekleme gerçekten rastgele mi:** Log zaman damgaları arasındaki fark `CropScanGapMinSeconds`-`CropScanGapMaxSeconds` aralığında mı (varsayılan 5-15 sn) kontrol edilmeli — bu, kullanıcının "insan gibi davranmalı" şartının canlıda karşılandığının tek kanıtı.
- [ ] **Bilinen sınır (kullanıcıya iletildi):** Tarama ortasında bot/uygulama yeniden başlarsa bellekteki ilerleme kaybolur; `CropScanEnable` görev bitmemişse DB'de açık kalır ama bir sonraki girişte otomatik DEVAM ETMEZ (bilinçli olarak RxQueue'ya eklenmedi) — kullanıcının checkbox'ı tekrar işaretleyip Save'e basması, bu da baştan taramayı yeniden başlatması gerekir.
- [ ] **Büyük mesafelerde süre:** max distance=15 gibi bir değer ~700 kareye denk geliyor, varsayılan 5-15 sn aralıkla ~1-2 saat sürebilir — kullanıcı bunu bilerek onayladı, ama ilk canlı denemede gerçekten bu kadar sürüp sürmediği gözlemlenebilir.

## Otomatik Tuzak (Trapper) Üretimi (2026-09-22) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Özellik:** `TrapTask` periyodik olarak (10-20 dk, köy bazlı `TrapEnable`) Min-Max rastgele bir parti tuzak üretiyor; binanın o anki seviyesinin izin verdiği tavanı (`TrapParser.GetMaxPossibleTraps` - `GetCurrentTrapCount`) hiç aşmıyor; kaynak yetersizse mevcut `TrainWhenLowResource` ayarına göre kısmi üretim/bekleme yapıyor. Bina seviye yükseltmesine (aynı sayfanın üst kısmı) dokunulmadı, o zaten genel Build Queue'da. Ayrıntı: CHANGELOG.md 2026-09-22.
- [ ] **Kullanıcı: derle + `dotnet test`.** `TrapParser` pure-test harness'ta 100/100 ama `BuildTrapsCommand`/`TrapTask`/dispatcher/RxQueue/XAML tarafı hiç derlenmedi (EF Core + WPF bağımlılığı, bu ortamda test edilemiyor) — şu ana kadarki en riskli teslimat bu.
- [ ] **Village Setting sekmesi:** "Trap settings" bölümü (Enable checkbox, repeat time, amount) görünüyor mu, değerler kayıt/yükleme arasında doğru gidip geliyor mu kontrol edilmeli.
- [ ] **Canlı gözlem:** İlk çalıştırmada log'da "Building N trap(s)" mesajı ve oyun içi tuzak sayısının gerçekten arttığı doğrulanmalı; Trapper'ı olmayan (Gaul olmayan) bir köyde görev sessizce atlanıyor mu kontrol edilmeli.

## Yağma Organize — Ganimete Göre Oto Asker Çarpanı (2026-09-21, ikinci) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Özellik:** 3 düşük (<%30) ganimet raporu üst üste -> ×0.8 (taban %40); 2 dolu (%100) rapor üst üste -> ×1.25 (tavan %200). Kullanıcının Min/Max'ı hiç değişmiyor, sadece gönderim anında ölçekleniyor. Ayrıntı: CHANGELOG.md 2026-09-21 (ikinci).
- [x] **Doğrulandı (2026-09-23):** Kullanıcı Raid List özetlerini ve logları kontrol etti — bot Raid List hedeflerini doğru okuyup işliyor. (Bkz. aşağıdaki "Okunmamış Yağma Raporları" maddesi — ayrı bir konuydu, bug değildi.)
- [ ] **Kullanıcı: derle + `dotnet test` + canlı gözlem.** Satır özetinde `troops X%` notunun beklendiği gibi hareket ettiği henüz teyit edilmedi.

## Yağma Organize — Rapordan Öğrenme Hazırlığı (2026-09-21) — Madde 2 Parser'ı Hazır, Bağlama Bekliyor

- [x] **Madde 2 hazırlığı:** `ScoutReportParser` + `ScoutReportRules.EstimatePlunderable` + fixture/testler (gerçek keşif raporu listesi ve detayından). Henüz hiçbir görev çağırmıyor, davranış değişmedi. Ayrıntı: CHANGELOG.md 2026-09-21.
- [ ] **Madde 2 bağlama:** Keşif listesini `RaidReportTask`'ta açma, detayı çözüp hedef koordinatını `MapTiles` ile bulma, giden keşifleri (ikon 15-17) satırla eşleme, sonucu satırda gösterme. Not: 16/17 ikonlarının anlamı henüz gerçek örnekle doğrulanmadı; listede koordinat yok, detay açmak şart.

## Bekleyen (Kullanıcı İsteğiyle Kenara Not Edildi, 2026-09-21/22) — Kod Yazılmadı

- [x] **Kapsamlı savaş karar motoru — Aşama A (Raid List), kodlandı 2026-09-27:** 3 açık soru cevaplandı (kendi raporlarım - yağma/saldırı/casus - hepsi dahil; hem Raid List hem gerçek Attack kapsanacak, kullanıcı ayar üzerinden seçecek; güncel keşif verisi yoksa eskisi gibi davran). `CombatPower.cs` + `CombatDecisionRules.cs` (saf kural, kabaca saldırı-puanı vs savunma-puanı) + `ReportTroopTableParser.cs` (paylaşımlı, saldırı VE casus raporlarındaki birlik tablosunu okur) + `ScoutedTargetGarrison` (yeni tablo, koordinat bazlı son-taranan-garnizon önbelleği) + `RaidReportTask`'a yeni bir casus-raporu geçişi (`ProcessScoutReports`, `/report/scouting`'i ilk kez açıyor) + `RaidListTask`'a opt-in gönderim kapısı (`EnableCombatCheckOnRaidList`, varsayılan KAPALI). Detay: PROJECT_CONTEXT.md §5zk, CHANGELOG.md 2026-09-27.
  - [ ] **Aşama B — gerçek Attack (SyncAttack/WaveAttack) tarafına bağlama:** `EnableCombatCheckOnAttack` ayarı ve `CombatDecisionRules` zaten hazır ama HİÇBİR Attack task'ına bağlanmadı — RaidListTask'ın "bu döngüyü atla, kendi periyoduna göre yeniden dene" deseni Sync/WaveAttackPlanTask'ın tek seferlik planlarına birebir oturmuyor, ayrı bir tasarım kararı gerekiyor.
  - [ ] **Doğrulanmamış varsayım — gerçek dolu-garnizonlu bir "troops" keşif raporu hiç yakalanmadı:** `ReportTroopTableParser`'ın casus raporundan gizli-olmayan birlik sayısı okuma mantığı yalnızca sıfır-garnizonlu bir gerçek yakalamaya (`ScoutReportDetail_Success.html`) ve saldırı raporundaki gizli (`?`) satıra karşı doğrulandı — dolu bir garnizonun gerçekten sayı olarak göründüğü bir rapor hiç görülmedi. `EnableCombatCheckOnRaidList`'i açan kullanıcı ilk canlı sonucu paylaşmalı (özellikle: taranan hedefte asker varken rapor sayıyı mı gösteriyor yoksa `?` mi kalıyor).
  - [ ] **Mısır/Hun `CombatPower` sayıları düşük güvenilirlikte:** Kaynak yalnızca "crop başına değer" oranı yayınlıyor, ham sayı değil — geri hesaplama Slave Militia/Ash Warden'da tam eşleşti ama Marksman/Marauder/Sopdu Explorer/Spotter ve tüm Mısır/Hun kuşatma birimleri (koç/mancınık/şef) doğrulanmadı. Bu tribe'lardan biriyle özellik kullanılacaksa oyun içi birim tooltip'iyle karşılaştırılmalı.
  - [ ] **Gerçek derleme/canlı test yapılmadı** (bu ortamda `dotnet` yok) — saf mantık kısmı `tools/pure-tests` ile 123/123 geçti, ama `RaidReportTask`/`RaidListTask`/`AppDbContext`/XAML tarafı yalnızca `csc -target:library` sözdizimi kontrolünden geçti (CS1xxx yok) — kullanıcının bir sonraki build + canlı gözlemde doğrulaması gerekiyor.
- [ ] **Raid List Bulk Add tekrar kontrolü (dedupe) yok:** Aynı yapıştırılan metin içinde tekrarlanan koordinat, veya zaten listede olan bir hedef, uyarı verilmeden ikinci bir satır olarak ekleniyor.
- [ ] **Raid List'te mevcut satır düzenlenemiyor:** `EntriesGrid` sadece Toggle active/paused ve Delete'i destekliyor; bir satırın Min/Max'ını değiştirmenin tek yolu Delete + yeniden Add (bu da o satırın birikmiş `ReportStatsJson`/asker çarpanını ve zamanlamasını sıfırlıyor). Olası çözüm tartışıldı (satır seçilince form dolsun, "Add" "Update"e dönüşsün) ama WPF/XAML tarafı bu ortamda derlenip doğrulanamayacağı için kullanıcı onayı bekleniyor.

## Bilgi Notu (Kod Değişikliği Yok): Çok Sayıda "Okunmamış" Yağma Raporu — Bug Değil (2026-09-23)

- Kullanıcı, `/report/offensive` listesinde çok sayıda kapalı-zarf (okunmamış) rapor birikmesini bot'un raporları işlemediğine dair bir belirti sandı. İncelemede netleşti: `RaidReportTask` raporun okunmuş/okunmamış bayrağını hiç kullanmıyor (bu bayrak yalnızca bir insan raporu tıklayıp açtığında değişiyor); oasis hedefli raporların koordinatı liste satırında zaten yazdığı için bot bunları hiç AÇMIYOR — bu yüzden sonsuza kadar "okunmamış" görünmeleri tasarım gereği ve zararsız. Kullanıcı Raid List özetlerini/logları kontrol edip botun gerçekte doğru çalıştığını doğruladı — aksiyon gerekmiyor.

## Raid List Kapı Erteleme "Sürüsü" (2026-09-20, üçüncü) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Bugfix (kullanıcı seçimi: 2. seçenek):** Kapı kapalıyken ertelenen satırlar artık hepsi aynı saniyeye değil, kapıdan başlayan kademeli slotlara (`RaidListDeferPlanner`) yerleşiyor. Ayrıntı: CHANGELOG.md 2026-09-20 (üçüncü).
- [ ] **Kullanıcı: derle + `dotnet test`.** Yeni `RaidListDeferPlannerTest` (6 test) ve `ArchitectureTest`.
- [ ] **Canlı gözlem:** Log'da `queued at` saatleri kapıdan itibaren gap kadar aralıklı mı; `is due, but another row already claimed...` satırları gönderim başına birkaç taneye indi mi.
- [ ] **Hesap ayarını kontrol et:** Loglardaki kapı aralıkları 577 sn ve 2054 sn'ydi (istenen 300-500 sn, varsayılan 30-90 sn değil) — Account Settings'teki `RaidListSendGapMin/MaxSeconds` değerleri veya çalışan sürüm doğrulanmalı.
- [ ] **Açık (dokunulmadı):** `VillageTaskBehavior` ertelenen (Skip) görevlerde de post-adımları (ToDorf/UpdateBuilding/UpdateStorage/UpdateQuest) çalıştırıyor; bu artık satır başına 1 tur ama tamamen ortadan kalkmadı. İstenirse "erteleme sinyali" ile sarmalayıcıdan çıkış eklenebilir (paylaşılan davranış dosyası, daha riskli).
- [ ] **Bilinen sınır:** Son slot yalnız bellekte; bayat kuyruk ihtimali `aktifSatır × gapMax` üst sınırıyla kırpılıyor, tam doğru olmayabilir (kısa süreli fazla bekleme yönünde).

## Ölü Hedef Hafızası (2026-09-20, ikinci) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Özellik (kullanıcı isteği):** "No village at these coordinates" alan hedef artık satır silinmeden `IsDeadTarget` olarak saklanıyor; aynı koordinatlı diğer/yeni satırlar sunucuya gitmeden yok sayılıyor. Ayrıntı: CHANGELOG.md 2026-09-20 (ikinci).
- [ ] **Kullanıcı: derle + çalıştır.** İlk açılışta yeni sütun yaması (ALTER TABLE) sorunsuz çalışıyor mu; ölü satır sekmede "DEAD TARGET" gösteriyor mu; aynı koordinatı tekrar ekleyince bot durmadan atlıyor mu.
- [x] **Karar verildi (kullanıcı):** İlk keşifte de bot durmuyor; hedef ölü işaretlenip pasife alınıyor ve bot yoluna devam ediyor.

## Raid List Gönderim Aralığı (2026-09-20) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Davranış değişikliği (kullanıcı isteği):** Gönderimler arası bekleme satırın 30-60 dk'lık tekrar aralığından ayrıldı; yeni hesap ayarı saniye cinsinden min-max (varsayılan 30-90 sn). Ayrıntı: CHANGELOG.md 2026-09-20.
- [ ] **Kullanıcı: derle + `dotnet test`.** Özellikle `AccountSettingInputTest` (enum sayısı) ve Account Setting sekmesindeki yeni alanın bağlanması.
- [ ] **Canlı gözlem:** Log'da `next send for ANY row not before` saatleri 30-90 sn arayla mı ilerliyor; troop biterken tüm liste duraklıyor mu (bu artık daha sık olabilir - gerekirse tek satır yerine "bekle" davranışı düşünülebilir).

## Yağma Organize — Kapsam A: Rapor Okuma + Satır Duraklatma (2026-09-19) — Kodlandı, Derleme/Canlı Doğrulama Bekliyor

- [x] **Özellik (2026-09-19, kullanıcı isteği, kapsam A seçildi):** `RaidReportTask` — `/report/offensive`'i okuyup her yeni yağma raporunu Raid List satırıyla eşliyor, sonucu satıra yazıyor (`RaidListEntry.ReportStatsJson`, Raid List sekmesinde görünüyor) ve kaybedilen yağmada (veya art arda 2 kayıplı yağmada) SADECE o satırı duraklatıyor. Ganimet oranı yalnızca kaydediliyor/gösteriliyor. Ayrıntı: CHANGELOG.md 2026-09-19 (ikinci), PROJECT_CONTEXT.md §5z.
- [x] **Canlı bugfix (aynı oturum):** `ChromeBrowser.Navigate()`/`Refresh()` — BiDi bağlamı bozulunca (aynı "no such frame" kimliği, retry'larla düzelmiyor, botu duraklatıyor) klasik sürücüyle fallback + `_bidiNavigationBroken` bayrağı.
- [ ] **Kullanıcı: derle ve `dotnet test` çalıştır.** Bu ortamda NuGet yok, tam derleme yapılamadı; yeni parser/kural/entity kodu + 54 test durumu gerçek HtmlAgilityPack ile bir shim üzerinden çalıştırıldı ve geçti, ama `RaidReportTask`/komutlar/ChromeBrowser/WPF tarafı yalnızca sözdizimi düzeyinde derlendi. Özellikle `ArchitectureTest` (task'ın hem `AppDbContext` hem komut `Handler`'ı alması — `RaidListTask`'la aynı kalıp) ve `HostingTest` (DI) kontrol edilmeli.
- [ ] **Gerçek kayıplı yağma satırı yakala.** Elimizdeki gerçek örnek yalnızca kaybedilmiş yağmanın DETAYI (pejken city, `lostWon`); liste satırı (`iReport3`/`iReport2` + carry ikonu) ve kısmi kayıplı bir yağmanın satırı/detayı yok. `MainCore.Test/Parsers/OffensiveReport/OffensiveReportList_Synthetic.html` bu yüzden SENTETİK (gerçek satırın yalnızca sonuç class'ı değiştirildi) — gerçek satır gelince değiştir.
- [ ] **Canlı doğrulama sırası:** (1) botu başlat → log'da "Raid report: first run - report N is the starting point" satırı; (2) bir sonraki gerçek yağma raporundan sonra Raid List satırında `| last report: …` görünmeli; (3) bilerek zayıf bir hedefe (ör. kaybettirecek) gönderim yapılırsa satır pasife düşmeli + Telegram.
- [ ] **Klasik-sürücü fallback'i canlıda doğrulanmadı.** Bir sonraki "no such frame" oluşumunda log'da "BiDi navigation failed twice … the classic driver worked" uyarısı görülmeli ve görev başarıyla tamamlanmalı. Bağlamın NEDEN bozulduğu hâlâ bilinmiyor (şüphe: `/hero/attributes`); log'daki iki kesin olay (08-25, 09-19) ikisi de `CheckHeroHealthCommand`'ın Navigate'i.
- [ ] **Bilinen sınırlama:** Rapor kontrol aralığı 15-25 dk sabit; Raid List satırı bundan kısa aralıklıysa kayıp veren bir satır tespit edilmeden bir kez daha gönderebilir. Ayrıca çok köylü hesapta, tek köydeki satırın hedefine BAŞKA bir köyden elle yağma atılırsa rapor yanlışlıkla o satıra yazılabilir (aynı koordinatı birden çok köyün satırı hedefliyorsa detay açılıp çözülüyor, tek köyse çözülmüyor).
- [ ] **Sonraki kapsamlar (ertelendi, kullanıcı A'yı seçti):** B = Map Analysis'ten filtreli hedef aday listesi + tek tıkla Raid List'e aktarma (keşif raporundaki "Cranny üstü yağmalanabilir" değeri hedef değerlendirmesine girdi olabilir); C = çok köylü dağıtım / asker havuzu bilinci / yolculuk süresi modeli (şu an tek köy var, gerek yok); ganimet oranına göre asker sayısını otomatik ayarlama (şu an yalnızca kayıt).
- [ ] **Not:** Oasis Scout ve kahraman-only vaha yağmaları (`0/0`, listede ganimet ikonu yok) bilerek göz ardı ediliyor — hiçbir Raid List satırının hedefi değiller.

## ChromeBrowser / Hero Attributes — İki Ayrı Canlı Bugfix (2026-09-18/19)

- [x] **Bugfix (2026-09-18, ikinci):** `ChromeBrowser.Navigate()`/`Refresh()`'in context-tazeleme sonrası retry'ı, aynı ölü context ID'sini tekrar buluyordu (Selenium'un `GetTreeAsync`'i canlı bir sorgu olduğu GitHub kaynağından doğrulandı — client cache bug'ı değil, tarayıcı-tarafı bir yarış durumu). `ContextRefreshRetryDelay=750ms` eklendi (retry'dan hemen önce). Değer keyfi/tahmini, tekrar ederse artırılabilir. Detay: CHANGELOG.md 2026-09-18 (ikinci).
- [x] **Bugfix (2026-09-19):** `HeroParser.IsAttributesPage`'in tek class kontrolü (`heroV2Attributes`) kullanıcının paylaştığı gerçek sayfa HTML'iyle uyuşmuyordu (gerçek class: `hero_inventoryAttributes`) — `CheckHeroHealthCommand` bu yüzden 180sn boyunca sürekli timeout alıyordu, sayfa görsel olarak tamamen doğru yüklenmiş olsa bile. Her iki class da artık kabul ediliyor; yeni bir regresyon test fixture'ı eklendi. Detay: CHANGELOG.md 2026-09-19.
- [ ] **İzlenmeli:** Her iki düzeltme de canlıda doğrulanmadı (`dotnet` bu ortamda yok). Bir sonraki gerçek oluşumda (context-stale hatası / hero attributes sayfası) sonucu kontrol et.
- [ ] **Netleşmemiş:** `hero_inventoryAttributes` class'ının ortaya çıkma tetikleyicisi (macera aktifken mi, bir Travian istemci güncellemesi mi — HTML'de görülen sürüm: gpack 632.2) netleşmedi; şimdilik iki class da kabul edilerek atlatıldı, tetikleyici tekrar merak edilirse araştırılabilir.

## Dodge Troops — Saldırıdan Önce Gönderime Yetişemiyordu (2026-09-18)

- [x] **Bugfix (2026-09-18):** Kullanıcının paylaştığı canlı log, `DodgeTroopTask`'ın gönderim ânında kendi işlem süresini (Rally Point'e gereksiz yeniden-navigasyon + sabit "5 saniye" eşiği) hiç hesaba katmadığını, bu yüzden saldırı düşmeden askerleri kaçıramadığını gösterdi. İki düzeltme yapıldı: (1) gönderim ânında `ToSendTroopsPageCommand`'ın tam sayfa yeniden-yüklemesi yerine doğrudan tab switch (zaten aynı binadayken), (2) sabit 5sn eşiği yerine `MinimumSendLeadTimeSeconds=15` + gerçek kalan süre yetersizse temiz bir "skip" (WaveAttackPlanner.SafetyBuffer emsaliyle tutarlı). Detay için CHANGELOG.md'deki 2026-09-18 kaydına bakın. Derleme yapılamadı (`dotnet` yok) — kullanıcı bir sonraki gerçek saldırıda canlı doğrulamalı: askerlerin artık saldırı düşmeden önce gerçekten gönderildiğini.
- [ ] **İzlenmeli:** `MinimumSendLeadTimeSeconds=15` tahmini bir değer (gerçek ölçülen ~9sn'ye pay eklenerek belirlendi) — canlıda hâlâ yetersiz çıkarsa (örn. yavaş bir bağlantıda `SendTroopsCommand`'ın kendi fill/submit/confirm adımları daha uzun sürüyorsa) artırılması gerekebilir.
- [ ] **Dokunulmadı, ayrı bir gözlem:** İlk canlı `attackSeconds` ölçümü ile gerçek varış arasında görülen ~25 saniyelik küçük sapmanın (bkz. CHANGELOG) kaynağı netleşmedi (sayfa/timer okuma hassasiyeti mi, başka bir etken mi) — şu anki düzeltme bu sapmayı telafi edecek kadar geniş bir pay bıraktığı için ayrıca araştırılmadı, ama tekrar ederse akılda tutulmalı.

## Canlı Bugfix: Dispatcher Switch'te Eksik Case'ler (2026-09-17)

- [x] **Tamamlandı (2026-09-17):** `CustomServiceScopeFactory.cs`'deki merkezi görev-dispatch switch'ine `HeroReviveTask.Task`, `SupplyForReviveTask.Task`, `OasisScoutTask.Task` için case eklenmemişti (09-16 birleştirmesi bu dosyaya hiç dokunmamıştı) — canlıda `NotImplementedException` ile Hero Revive'ı her tetiklendiğinde durduruyordu. 3 case eklendi. Kullanıcı canlıda doğrulamalı.
- [ ] **Tekrarlayan tuzak — dikkat:** Bu, aynı switch'te eksik-case hatasının ÜÇÜNCÜ tekrarı (bkz. CHANGELOG.md 2026-08-20 ilk kez RaidListTask için, 2026-09-17 ikinci kez bu üçü için). Yeni bir Task tipi eklendiğinde bu switch'i güncellemeyi unutmak kolay oluyor — CLAUDE.md'ye bu konuda kalıcı bir hatırlatma eklenmesi düşünülebilir (kullanıcı isterse).

## Büyük Birleştirme: hero-revive + hero-revive-buildfix + oasis-scout + raid-list (2026-09-16)

- [x] **Tamamlandı (2026-09-16):** Dört ayrı patch (hero-revive-2026-09-12, hero-revive-buildfix-2026-09-15, oasis-scout-2026-09-15, artı bu oturumun r-fix + raid-list'i) birbirinden habersiz aynı paylaşılan dosyalara (HeroParser.cs, VillageSettingEnums.cs, AppDbContext.cs, VillageSettingInput.cs, RxQueue.cs, VillageSettingTab.xaml(.cs), AccountSettingEnums.cs) dokunuyordu, elle uygulanınca 10 build hatasına yol açtı. Hepsi tek, tutarlı 30 dosyalık bir sette elle birleştirildi (`merged-2026-09-16.zip`, detay `OKUBENI.md`'de). **Build başarılı** (kullanıcı GitHub Actions'ta doğruladı).
- [ ] **İzlenmeli:** Bundan sonra hero-revive/oasis-scout/raid-list ailesine dokunan HERHANGİ bir yeni değişiklik, artık bu birleştirilmiş 30 dosyalık hâli temel almalı — o üç özelliğin ayrı zip'lerini (özellikle HeroParser.cs, VillageSettingEnums.cs, AppDbContext.cs, VillageSettingInput.cs, RxQueue.cs, VillageSettingTab.xaml(.cs)) tekrar bağımsız/tam-dosya olarak uygulamak aynı çakışmayı yeniden yaratır.
- [ ] **Doğrulanamayan (kod derlense de canlı test edilmedi):** Oasis Scout'un hayvan gücü tahmini (`AnimalPower.cs`) gerçek combat simulator değil, kaba bir tahmin; cooldown yok (kullanıcı isteğiyle); `HeroParser.GetHomeVillageGameId` tek örnekle doğrulanmış, kahraman başkenti dışında bir köyde evdeyken doğrulanmamış.

## Raid List — Hesap Genelinde Sıralı Gönderim Zinciri (2026-09-16)

- [x] **Özellik (2026-09-16):** Bulk-add ile eklenen büyük listelerde (ör. 50 koordinat) satırların bağımsız rastgele zamanlaması yüzünden gönderimlerin art arda kümelenmesi düzeltildi. Hesap genelinde paylaşılan bir "sonraki gönderim en erken ne zaman" kapısı eklendi (`AccountSettingEnums.RaidListNextAllowedSendAtMinutes`) — her gönderimden sonra o satırın kendi min-max aralığı kadar ileri itiliyor, kapı geçmeden başka satır gönderim yapamıyor. Detay için CHANGELOG.md'deki 2026-09-16 kaydına bakın. Derleme yapılamadı (`dotnet` yok) — kullanıcı canlıda doğrulamalı: büyük bir listede gönderimlerin artık gerçekten aralıklı gittiğini.
- [ ] **İzlenmeli:** Kapı hesap genelinde (village bazlı değil) — birden fazla köyden farklı hedeflere yağma gönderen bir kurulumda da tüm köylerin raid list gönderimleri aynı ortak kapıyı paylaşıyor. Kullanıcı bunun istenen davranış olduğunu belirtti (tek tarayıcı/oturum = tüm hesap aktivitesi tek elden akmalı), ama pratikte çok-köylü kurulumlarda beklenenden yavaş hissettirirse village-bazlı bir kapıya bölünmesi istenebilir.

## Kahraman Envanteri (Heybe) ile Eksik Kaynak Tamamlama — Kapasite Aşımı Düzeltmesi (2026-09-12)

- [x] **Bugfix (2026-09-12):** `UseHeroResourceCommand.RoundUpTo100()` (eksik kaynağı 100'e yuvarlama — kullanıcı isteğiyle zaten var, "fazlası olabilir") yuvarlama sonrası hedef miktarı depo/ambar KAPASİTESİYLE karşılaştırmıyordu — kapasiteye yakın senaryolarda yuvarlama hedefi kapasitenin üzerine taşırabiliyordu. `ClampToStorageCapacity` eklendi (`UseHeroResourceCommand.cs`), `Command`'a `VillageId` eklendi, `HandleResourceCommand.cs` çağrısı güncellendi. Derleme yapılamadı — kullanıcı doğrulamalı.
- [x] **Analiz (2026-09-11/12):** `ClaimQuestCommand.cs`'deki iki önceki-analiz tutarsızlığı (asimetrik gecikme, karışık döngü sinyali) tekrar kontrol edildiğinde dosyada zaten giderilmiş bulundu — kod değişikliği gerekmedi.
- [ ] **Hâlâ doğrulanmayan:** `browser.Input()`/`SendKeys`'in gerçekten etkin bir `resourceTransferDialog` input'una karşı çalıştığı — kullanıcının paylaştığı gerçek dialog HTML'i input'un disabled OLMADIĞINI doğruladı (önceki "depo dolu" fixture'ındaki disabled hâli sadece o kenar duruma özelmiş), ama canlı bir tam akış (heybeden gerçek transfer) sonucu henüz gözlemlenmedi/paylaşılmadı.

## "Ne ingame ne login" Artık Durdurmuyor, 30dk'da Bir Retry (2026-09-11, ikinci)

- [x] **Özellik (2026-09-11, ikinci):** Kullanıcı isteğiyle, sayfa ne oyun içi ne login sayfasıysa (örn. `ERR_CONNECTION_TIMED_OUT` gibi geçici bağlantı kopmaları) bot artık hesabı tamamen durdurmuyor — aynı görevi 30 dakika sonraya erteleyip kendi kendine tekrar deniyor, üst sınır yok. `AccountTaskBehavior.cs`'de `Stop.Error` → `Skip.Error` + `request.ExecuteAt += 30dk` oldu. Derleme yapılamadı — kullanıcı canlıda doğrulamalı: gerçek bir bağlantı kopmasında hesabın artık `Paused`'a düşmediğini, görevin 30 dakika sonra tekrar denendiğini.
- [ ] **Bilinçli ödün, izlenmeli:** Gerçekten geçici olmayan durumlar (hesap banlı, sayfa yapısı kalıcı değişmiş) da artık sessizce sonsuza kadar 30dk'da bir retry edilecek, eskisi gibi bildirimle durmayacak. Sorun çıkarsa "N denemeden sonra yine de pes et ve bildir" sınırı eklenmeli.

## Maksimum Seviye Üzerinde Yükseltme İşi Sonsuz Retry (2026-09-11)

- [x] **Bugfix (2026-09-11):** Village 2011'deki Marketplace zaten gerçek maksimum seviyedeydi (20, "completely extended"), ama kuyruktaki iş bunu hedef seviyeye ulaşmış saymıyordu (muhtemelen hedef, sınırsız `NormalLevel` metin kutusundan 20'nin üzerinde girilmişti) — bot 180sn'de bir `GetUpgradeButton` arayıp sonsuza kadar retry yiyordu. `ValidateJobCompleteCommand.cs`'e güvenlik ağı eklendi (bina gerçek maksimumdaysa iş her zaman tamamlandı sayılır), `NormalBuildCommand.cs`'de yeni işler artık `GetMaxLevel()` ile otomatik sınırlanıyor. Derleme yapılamadı — kullanıcı doğrulamalı: village 2011'deki iş bir sonraki çalıştırmada kendiliğinden kuyruktan silinip silinmediğini.
- [ ] Aynı sınırsız-metin-kutusu sorunu `ResourceBuildPlan`'ın `ResourceLevel` alanında da mevcut (aynı `BuildTab.xaml`, farklı TextBox) — kaynak alanları (tarla) için de teorik olarak aynı sonsuz-retry riski var, ama henüz canlıda gözlemlenmedi/doğrulanmadı, bu yüzden dokunulmadı.

## Çerez Onayı — Shadow DOM (2026-09-09) — CANLIDA DOĞRULANDI (2026-09-10)

- [x] **Bugfix (2026-09-09) — canlıda doğrulandı (2026-09-10):** Bazı sunucularda (`ts12.x1.europe.travian.com` dahil, kullanıcı DevTools yakalamasıyla doğruladı) CMP "Accept all" butonu `#cmpwrapper` altında açık bir Shadow DOM içinde render ediliyor — `HtmlAgilityPack` tabanlı `CookieConsentParser.GetAcceptAllButton`, `driver.PageSource`'ta bunu hiç göremiyordu, her zaman `null` dönüp sessizce no-op yapıyordu. `DismissCookieConsentCommand` artık tespit+tıklamayı tek bir JS script'inde yapıyor (`#cmpwrapper?.shadowRoot?.querySelector(...) ?? document.querySelector(...)`), her döngüde koşulsuz çalışıyor. Kullanıcı Chrome profil klasörünü (`Data\Cache\{hesap}\default`) silip popup'ı zorla tekrar tetikledi — bot popup'ı gerçekten kapattı, login formu dolduruldu, görevler başladı.
- [x] **REGRESYON — düzeltildi (2026-09-09, ikinci) — canlıda doğrulandı (2026-09-10):** Analiz sırasında, 2026-08-26 (üçüncü)'da eklenip canlıda doğrulanmış olan `AccountTaskBehavior.cs`'deki `_browser.Html` okumalarına ait `try/catch(WebDriverException)` → `BrowserClosed.Error` koruması mevcut zip'te bulunmuyordu (2026-09-05'teki yeniden düzenlemeyle kaybolmuştu). `try/catch` bloğu geri getirildi ve yeni koşulsuz `DismissCookieConsentCommand` çağrısını da (o da `browser.Html` okuyor/JS çalıştırıyor) kapsayacak şekilde genişletildi. Kullanıcı Chrome'u kasıtlı kapattı — görev zamanı gelince bot Chrome'u kendi açtı, hesap bilgilerini girdi, görevler kaldığı yerden devam etti.

## Login Formu Element Bulma (2026-09-08)

- [x] **Bugfix (2026-09-08):** Turnuva sunucusunda (`ttq.x2.europe.travian.com`) login ekranı, formdan önceki satır-içi SVG logo yüzünden `HtmlAgilityPack`-tabanlı pozisyonel XPath'in gerçek DOM ile uyuşmamasına takılıp 180sn boyunca kullanıcı adı input'unu bulamıyordu. `LoginCommand` artık kullanıcı adı/şifre/login-butonu için native Selenium `By.Name`/`By.XPath` locator'ları kullanıyor, HtmlAgilityPack'e hiç uğramıyor. Derleme/canlı test yapılamadı — kullanıcı doğrulamalı.

## Çerez Onayı (Cookie Consent) Modalı (2026-09-03/04/05)

- [x] **Bugfix (2026-09-05):** `AccountTaskBehavior`'daki çerez-modalı-kapatma çağrısı `IsIngamePage` şartının içine hapsolmuştu, login sayfasındaki modalı hiç kapatmıyordu — bu da kullanıcının bildirdiği `LoginCommand`/`WaitPageChanged("dorf")` 180sn timeout'unun (giriş gerçekte başarılıyken botun bunu görememesi) kök nedeniydi. Çağrı artık login/oyun-içi ayrımından önce, koşulsuz çalışıyor. Derleme/canlı test yapılamadı (`dotnet` yok) — kullanıcı doğrulamalı: login sırasında modal çıktığında artık timeout yemeden `dorf`'a geçtiğini.
- [ ] Bu düzeltmeyle birlikte, daha önce ayrı bir teori olarak flaglenen `SwitchTabCommand` 180sn timeout'unun (Raid List/Sync Attack/Wave Attack/Dodge'ı etkileyen) de aynı kökten (modal interception) kaynaklanıp kaynaklanmadığı hâlâ doğrulanmadı — 09-04'teki fix zaten oyun-içi sayfaları kapsıyordu, o yüzden bu ayrı XPath/index-kayması teorisi hâlâ canlı olarak elenmedi.

## Otomatik Yerleşim/İnşa (AutoSettle & ApplyBuildTemplate) (2026-08-26/27 — analiz edildi, kod değişikliği yapılmadı)

- [ ] **Analiz edildi, düzeltilmedi — kullanıcı gerçek talep:** Kullanıcı, yeni kurulan köylere gömülü varsayılan inşa şablonunun (`ApplyBuildTemplateTask`) otomatik uygulanmasını **iptal etmek** istiyor — görevleri kendisi manuel import edecek. Kodda bunun için açma/kapama ayarı yok, 2026-08-15 tarihli bilinçli bir tasarım kararıyla hep-açık bırakılmış (`ApplyBuildTemplateTask.cs` başındaki yorum: "No enable/disable setting by design"). Kullanıcı aynı mesajda "kod yazma" dediği için bu değişiklik **henüz yapılmadı** — onay bekleniyor. Onay gelince: `UpdateVillageListCommand.cs`'deki `foreach (var newVillageId in newVillageIds) { taskManager.AddOrUpdate<ApplyBuildTemplateTask.Task>(...); }` bloğunun kaldırılması (veya bir ayarla korunması) yeterli olur.
- [ ] **Kullanıcının bildirdiği asıl sorun, kod incelemesiyle bir teoriye indirgendi ama DOĞRULANMADI:** "Yeni köy hiç job almıyor." Zincir takip edildi: `MissingBuildingVillagesSpec` bir köyü `Buildings.Count != 40` olduğu sürece "taranmamış" sayıp `UpdateBuildingTask`'ı sürekli yeniden kuyruğa alıyor; `ApplyBuildTemplateTask.CanStart` ise özel olarak `Location == 40` (sur) satırının var olmasını bekliyor. Eğer `BuildingLayoutParser.GetInfrastructures` (dorf2 parser'ı, 22 `buildingSlot` div bekliyor) taze kurulmuş bir köyün dorf2 sayfasında farklı/eksik bir yapı nedeniyle sur konumunu (40) hiç yakalayamıyorsa, bina sayısı asla 40'a ulaşmaz → `UpdateBuildingTask` hatasız ama sonuçsuz şekilde sonsuza dek yeniden kuyruğa girer, `ApplyBuildTemplateTask` asla başlamaz. Kullanıcı `Auto load village building` ayarının **açık** olduğunu doğruladı (yani bilinen en basit neden — ayarın kapalı olması — elendi). Şablonun kendisi de kontrol edildi (kullanıcının yüklediği `.tbs` dosyası gömülü `DefaultBuildTemplate.json` ile birebir aynı çıktı — 63 job, sur job'u pozisyon 15'te mevcut) — şablon sorunu değil.
- [ ] **Doğrulamak için gereken:** Taze kurulmuş bir köyün gerçek dorf2 sayfası HTML'i (kullanıcıdan istendi, henüz gelmedi) — VEYA o köyün `Buildings` tablosundaki gerçek satır sayısı/log'da `Update building` görevinin sonucu. Bu gelmeden kod değişikliği yapılmadı.

## Village Not Found / Görev Kuyruğu Anomalileri (2026-08-27 — gerçek log ile analiz edildi, kod değişikliği yapılmadı)

- [ ] **Kesin doğrulanan kod noktası:** `SwitchVillageCommand.cs`, hedef köy panelde bulunamayınca `Skip.Error.WithError("Village not found")` dönüyor ama görevin `ExecuteAt`'ini hiç değiştirmiyor. `TimerManager`'ın Skip işleme mantığı (`if (task.ExecuteAt == cacheExecuteTime) Remove(...) else ReOrder(...)`) bu yüzden görevi **erteleme yapmadan doğrudan kuyruktan siliyor** (login-skip durumundaki gibi `ExecuteAt` ileri alınmıyor).
- [ ] **Çözülmemiş çelişki:** Kullanıcının paylaştığı gerçek log'da (`ttq.x2`/`ts11.x1` hesapları, köy 2010/2011) aynı köy için "Upgrade building" görevi silinmesine rağmen birkaç saniye/dakika sonra **tekrar** ortaya çıkıp yine "Village not found" ile başarısız oluyor — yani bir yerde bu görev köyün varlığı doğrulanmadan **yeniden** kuyruğa ekleniyor. Bu "yeniden ekleyen" mekanizma kodda henüz bulunamadı (muhtemelen job kuyruğunu periyodik tarayan bir yer, köyün hâlâ hesaba ait olup olmadığını kontrol etmiyor).
- [ ] **Netleşmesi gereken:** 2010/2011 numaralı köyler hangi hesaba ait (kayıp/terk edilmiş bir köy mü, yoksa hesaplar/server'lar arası bir ID karışıklığı mı) — kullanıcıdan bekleniyor, netleşince hem "yeniden ekleyen" kaynağı hem gerçek düzeltme (Skip'te ExecuteAt'i ileri almak mı, yoksa köy gerçekten yoksa ilişkili job'ları da temizlemek mi) belirlenecek.

## Kaynakta Yeterlilik Olmasına Rağmen Görev Yapılmaması (2026-08-27 — henüz başlanmadı)

- [ ] Kullanıcı bildirdi: bazen köyde yeterli kaynak olduğu hâlde bir görev (hangi tür — inşaat mı, birim eğitimi mi — netleşmedi) yerine getirilmiyor. Hangi görev türü, gerçek log kaydı ve mümkünse köyün o anki kaynak/job durumu istendi, henüz gelmedi. Analiz başlamadı.

## Login (2026-08-26'da bugfix)

- [x] **Bugfix (kullanıcı gerçek sayfa HTML'i ile bildirdi, 2026-08-26):** Travian login/lobby ekranını yeniden tasarlamış (yeni markup "V2" son ekli class'lar, eski `id="loginScene"` kapsayıcı kaldırılmış) — bot login sayfasında sonsuza dek takılı kalıyordu. `LoginParser.GetLoginButton` artık `loginScene`'e değil şifre input'una çapalanıp üst ataları tırmanarak `.green` butonu buluyor; hem eski hem yeni tasarımda çalışıyor. Yeni fixture (`LoginPageV2.html`) + parametrize testler eklendi. Ayrıntılar için CHANGELOG.md'deki 2026-08-26 (ikinci) kaydına bakın. Derleme/canlı test yapılamadı (`dotnet` yok, Python'da simüle edilerek doğrulandı) — kullanıcı doğrulamalı: TBS artık login ekranında takılmadan içeri giriyor mu.

## Tarayıcı / Selenium Altyapısı

- [x] **Bugfix (kullanıcının kendi bot log'uyla, 2026-08-26, üçüncü):** "invalid session id" (`OpenQA.Selenium.WebDriverException`) — tarayıcı oturumu iki görev arası boşta ölünce (`IsOpen` sadece yerel `_driver is not null`, bunu yakalamıyor), bir sonraki görevin ilk satırındaki `_browser.Html` okuması ham exception fırlatıyor, 25 Ağustos'taki `BrowserClosed` kurtarma yolundan (o sadece `BiDiException`'ı yakalıyordu) hiç geçmeden hesabı direkt duraklatıyordu. `AccountTaskBehavior.cs`'deki iki `_browser.Html` okuması artık `try/catch (WebDriverException)` ile `BrowserClosed.Error`'a yönlendiriliyor. Ayrıntılar için CHANGELOG.md'deki 2026-08-26 (üçüncü) kaydına bakın. Derleme/canlı test yapılamadı (`dotnet` yok) — kullanıcı doğrulamalı: oturum öldüğünde artık hesap duraklamadan Chrome'un otomatik açılıp devam ettiğini.
- [ ] Bu düzeltme sadece `AccountTaskBehavior`'daki (görev başlamadan önce/hemen sonra) iki okumayı kapsıyor — görev SIRASINDA (bir Command/Parser içinde) oturum ölürse aynı tür ham exception teorik olarak hâlâ yakalanmadan kaçabilir; şimdilik gerçek bir "task ortasında ölür" log'u yok, gelirse ayrıca ele alınmalı.
- [x] **Bugfix (kullanıcının kendi bot log'uyla, 2026-08-25, ikinci):** `no such frame: Context ... not found` — `ChromeBrowser._context`, `Setup()` sırasında bir kez yakalanıp hiç yenilenmiyordu; o ilk sekme/pencere herhangi bir sebeple kaybolursa `Navigate`/`Refresh` kalıcı olarak aynı hatayla başarısız oluyordu (Polly aynı ölü context'i 3 kez boşuna deniyordu). `Navigate`/`Refresh` artık bu hatayı yakalayıp güncel context'i yeniden çözüp bir kez daha deniyor; hiç context kalmamışsa net bir `Stop.Error` dönüyor. Ayrıntılar için CHANGELOG.md'deki 2026-08-25 (ikinci) kaydına bakın. Derleme/canlı test yapılamadı (`dotnet` yok) — kullanıcı doğrulamalı: hatayı tekrar görürse botun kendini toparlayıp devam edip etmediği.
- [ ] Context'in NEDEN kaybolduğu netleşmedi (elle kapatma, headless çökme, popup vb.) — tekrar ederse kök nedene bakmak gerekebilir. Ayrıca aynı gün loglanan, henüz ayrı incelenmemiş `SwitchTabCommand` timeout bulgusuyla (aşağıda, Raid List bölümünde) ilişkili olup olmadığı belirsiz — ikisi de tarayıcı/sayfa durumunun beklenmedik şekilde değiştiği anlarda ortaya çıkıyor.

## Harita Analizi (Map Analysis) (2026-08-21 başlandı ve tamamlandı)

- [x] Kullanıcı isteği: verilen bir koordinata mesafeye göre sıralı, nüfus/oyuncu ismi/ırk/ittifak bilgili köy listesi. `MainCore/Parsers/MapSqlParser.cs` (sunucunun herkese açık `map.sql`'ini parse eden saf statik sınıf) + `MainCore/UI/ViewModels/Tabs/MapAnalysisViewModel.cs` (X/Y/Max girişi, düz `HttpClient` ile `{server}/map.sql` indirip Öklid mesafesine göre sıralayan Search komutu) + `WPFUI/Views/Tabs/MapAnalysisTab.xaml(.cs)` + `AccountTabStore`/`MainLayoutUc` kayıtları eklendi. Format ve sütun sırası, kullanıcının gerçek `map.sql`'i (Europe 11, 5721 satır) üzerinde tek tek doğrulandı; parser mantığı Python'da simüle edilip tüm dosya üzerinde (5721/5721, kayıpsız) test edildi, kullanıcının kendi hedef koordinatıyla arandığında kendi köyü doğru mesafeyle listenin başında çıktı. Ayrıntılar için CHANGELOG.md'deki 2026-08-21 (bu konudaki) kaydına bakın.
- [ ] Derleme/canlı test yapılamadı (`dotnet` yok) — kullanıcı Windows/CI'da derleyip "Map analysis" sekmesini canlı test etmeli.
- [ ] Bu sunucuda `map.sql` herkese açık ve kimlik doğrulamasızdı — farklı bir sunucuda dosya kapalı/farklı yolda olabilir, bu durumda ne olacağı henüz canlı doğrulanmadı (kod bir hata kutusu göstermek üzere yazıldı, ama denenmedi).
- [x] ⚠️ **Canlı bug düzeltildi (kullanıcı ekran görüntüsüyle bildirdi, 2026-08-24, ikinci):** Arama "50 sonuç bulundu" diyordu ama liste tamamen boş görünüyordu — `ResultsList`'in, uygulamadaki her diğer listeden farklı olarak hiç `ItemTemplate`'i yoktu, yazı rengi MaterialDesign'ın temaya bağlı varsayılanına kalmıştı ve bu sekmenin özel açık gri fonuyla neredeyse aynı çıkıyordu. Açıkça `Foreground="Black"` diyen bir `ItemTemplate` eklendi. Ayrıntılar için CHANGELOG.md'deki 2026-08-24 (ikinci) kaydına bakın. Derleme/canlı test yapılamadı (`dotnet` yok, sadece XAML well-formedness kontrol edildi) — kullanıcı doğrulamalı: sonuç satırları artık görünüyor mu.

## Yağma Listesi (Raid List) (2026-08-18 başlandı, aynı gün UI tamamlandı)

- [x] **Davranış değişikliği (kullanıcı isteği, 2026-08-25, üçüncü):** Bir satırın askeri (veya istenen kahramanı) yetmiyorsa artık sadece o satır atlanmıyor — hesabın TÜM aktif Raid List satırları duraklatılıyor (`IsActive=false` + kuyruktaki görevler kaldırılıyor, mevcut "Pause all" ile aynı mantık) ve `NotifyOnPause` açıksa Telegram'dan mesaj gönderiliyor. Sadece Raid List özelliği duruyor, botun geri kalanı etkilenmiyor. Ayrıntılar için CHANGELOG.md'deki 2026-08-25 (üçüncü) kaydına bakın. Derleme/canlı test yapılamadı (`dotnet` yok) — kullanıcı doğrulamalı: TÜM satırların pasife düştüğü, Telegram mesajının geldiği, botun geri kalanının etkilenmediği.
- [x] **Davranış değişikliği (kullanıcı isteği, kullanıcının kendi bot log'uyla, 2026-08-25):** "No village at these coordinates" hatasıyla reddedilen bir satır artık sessizce atlanmıyor — satır Raid List'ten tamamen siliniyor VE bot tamamen duruyor (Skip değil Stop). Kullanıcının paylaştığı log'da iki ölü hedef ((35|-9), (48|-12)) sabah boyunca defalarca vurulmuştu, ban riski endişesiyle istendi. Ayrıntılar için CHANGELOG.md'deki 2026-08-25 kaydına bakın. Derleme/canlı test yapılamadı (`dotnet` yok) — kullanıcı doğrulamalı: bilinen ölü bir hedefle botun gerçekten Stop olduğu, satırın listeden kaybolduğu, diğer satırların etkilenmediği.
- [ ] **Ayrı, henüz dokunulmamış bulgu (aynı log'dan, 2026-08-25):** Log'da Raid List'ten bağımsız, muhtemelen daha ciddi bir sorun var — `SwitchTabCommand`, Rally Point'in ikinci tab'ını (`SwitchTabCommand {"TabIndex":"2"}`) bulamayıp art arda 4 kez 180 saniye timeout yiyor, Polly'nin 3 retry hakkı tükenince bot gerçekten duruyor (06:43 screenshot). Bu sabah botu asıl durduran şey boş-köy hedefleri değil, buydu. Rally Point tab'ını kullanan her özelliği (Sync Attack, Wave Attack, Dodge, Raid List) etkileyebilir — kullanıcı isterse ayrı bir istek olarak incelenmeli (örn. tab XPath'inin artık geçerli olmadığı, sayfanın farklı yüklendiği, ya da network/gecikme kaynaklı gerçek bir timeout mu olduğu netleştirilmeli).
- [x] `Entities/RaidListEntry.cs` (yeni tablo), `Tasks/RaidListTask.cs` (satır-başına bağımsız tekrarlayan görev, kendi kendini `random(min,max)` dakika ile yeniden zamanlıyor), `UpdateStorageCommand.cs`'e bootstrap eklendi.
- [x] ⚠️ Kritik altyapı düzeltmesi: proje EF migration kullanmıyor, `EnsureCreatedAsync()` mevcut DB'lerde yeni tabloyu oluşturmuyordu — `AppDbContext.EnsureRaidListEntriesTableExists()` ile (EF'in kendi `GenerateCreateScript()`'inden) güvenle eklendi.
- [x] **CRUD komutları + WPF UI sekmesi eklendi:** `RaidListViewModel.cs` (ekle/toggle-active/sil, mevcut Sync/Wave Attack UI desenleri yeniden kullanılarak) + `RaidListTab.xaml`/`.xaml.cs` + `AccountTabStore`/`MainLayoutUc` kayıtları. Ayrıntılar için CHANGELOG.md'deki 2026-08-18 (ikinci) kaydına bakın.
- [x] ⚠️ **Canlı bug düzeltildi (kullanıcı bildirdi, 2026-08-20):** Her Raid List tetiklemesi `NotImplementedException: Task Task is not implemented` ile 3 retry sonrası botu tamamen durduruyordu — kök neden, `RaidListTask.Task`'a `CustomServiceScopeFactory.cs`'deki dispatcher switch'inde hiç `case` eklenmemiş olmasıydı (CRUD/UI oturumunda atlanmış bir adım). `case RaidListTask.Task` eklendi. Ayrıntılar için CHANGELOG.md'deki 2026-08-20 kaydına bakın. Derleme/canlı test yapılamadı — kullanıcı doğrulamalı.
- [x] **Özellik eklendi (2026-08-20, ikinci):** Köyde satırın istediği kadar asker yoksa, bot artık duraklamak yerine o satırı sessizce atlayıp bir sonraki göreve geçiyor (satır kendi `random(min,max)` aralığında yeniden zamanlanıyor). `RaidListTask.HandleAsync` gönderim öncesi kendi asker/kahraman kontrolünü yapıyor, `SendTroopsCommand` değişmedi. Ayrıntılar için CHANGELOG.md'deki 2026-08-20 (ikinci) kaydına bakın. Derleme/canlı test yapılamadı — kullanıcı doğrulamalı.
- [x] **Gerçek düzeltme (kullanıcı canlı log + gerçek sayfa capture'ı ile, 2026-08-22):** Yukarıdaki 2026-08-21 düzeltmesi `div.errorMessage` varsayımıyla canlıda hiç çalışmıyordu — kullanıcının paylaştığı gerçek capture, hatanın aslında `<p class="error">There is no village at these coordinates.</p>` olduğunu gösterdi (formun dışında, `</form>`'dan sonra). `GetErrorMessage` artık ikisini de kontrol ediyor. Ayrıca `MainCore.Test/Parsers/RallyPointSendTroopsParserTest.cs` + gerçek capture'dan fixture eklendi (bu proje için ilk parser fixture testi). Ayrıntılar için CHANGELOG.md'deki 2026-08-22 kaydına bakın. Derleme/canlı test yapılamadı (`dotnet` yok) — kullanıcı doğrulamalı.
- [x] **Yeni özellik (kullanıcı isteği, 2026-08-22, ikinci):** Her koordinat için asker sayısı artık her gönderimde `Min-Max` aralığından yeniden rastgele seçiliyor (önceden satır oluşturulurken belirlenen sabit bir sayıydı), koordinat başına birden fazla asker türü karışık desteklenir, ve çok satırlı bir koordinat listesi (`[X|Y]` formatı) yapıştırılıp tek seferde toplu satır eklenebiliyor (hepsi aynı köyden, ilk gönderimleri de birbirine yayılmış şekilde). Var olan DB'ler için `ALTER TABLE` yaması eklendi (yeni `TroopAmountRangesJson` kolonu), eski sabit-sayı satırları hiç dokunulmadan çalışmaya devam ediyor (geriye dönük uyumluluk fallback'i). Ayrıntılar için CHANGELOG.md'deki 2026-08-22 (ikinci) kaydına bakın. Derleme/canlı test yapılamadı (`dotnet` yok) — kullanıcı doğrulamalı: var olan bir DB'de açılışta ALTER TABLE, UI'daki Min/Max ve Bulk add, canlıda gerçekten değişen asker sayısı.
- [ ] Derleme/canlı test yapılamadı (`dotnet` yok) — kullanıcı Windows/CI'da doğrulamalı (backend, UI, dispatcher düzeltmesi, "yetersiz asker → sessiz atlama" ve "geçersiz hedef → sessiz atlama, doğru selector'la" davranışları dahil).
- [ ] **Ayrı istek (henüz başlanmadı):** Göçmen için sıralı yedek koordinat listesi (1. dolu → 2.'yi dene → ...), `AutoSettleTask`'ın tek `AutoSettleTargetX/Y`'si yerine geçecek.
- [x] **Yeni özellik (kullanıcı isteği, 2026-08-24):** Hesap-geneli `Pause all`/`Play all`/`Clear all` butonları eklendi — koordinata özel `Toggle active/paused`/`Delete` butonlarına dokunulmadı, bunların yanına listenin tamamını tek tıkla duraklatan/başlatan/silen 3 buton geldi. Ayrıntılar için CHANGELOG.md'deki 2026-08-24 kaydına bakın. Derleme/canlı test yapılamadı (`dotnet` yok) — kullanıcı doğrulamalı: özellikle `Play all`'ın duraklamış satırları doğru şekilde yeniden kuyruğa aldığı ve `Clear all`'ın onay kutusundan geçtiği.
- [ ] **Bilinen ama henüz dokunulmayan tutarsızlık:** `SendReinforcementCommand.cs` (2026-08-13/14'te "silindi" diye belgelenmişti) hâlâ zip'te duruyor, hiçbir yerden çağrılmıyor — zararsız ölü kod ama belge/kod tutarsızlığı, kullanıcı isterse ayrı bir istekle silinebilir.
- [x] ⚠️ **Canlı bug düzeltildi (kullanıcı bildirdi, 2026-08-18 → aynı gün kodlandı):** İki ayrı belirti aynı kökten geliyordu — `TrainTroopParser.GetPresentAmount`'ın doğrulanmamış HTML okuması yanlış sayı döndürüyordu: (1) "göçmen yetiştir + göç et" tek tikte, hiç göçmen yetiştirmeden doğrudan köy kurma denemesine giriyordu; (2) köy kurduktan sonra, göçmen kalmadığı halde sanki hâlâ 3 göçmen varmış gibi tekrar deniyordu. **Çözüm:** hazır göçmen sayısı artık HTML'den her seferinde yeniden okunmuyor — bot'un kendi yazdığı/güncellediği `VillageSettingEnums.AutoSettleSettlersReady` sayacı tutuluyor (sadece onaylı `TrainSettlerCommand` sonrası artırılıyor, onaylı köy kurma sonrası 0'a sıfırlanıyor). Köy kurma başarıyla tamamlanınca `AutoSettleEnable` de otomatik kapatılıyor (istenen ikinci düzeltme de böylece kodlandı). Ayrıntılar için CHANGELOG.md'deki 2026-08-18 (üçüncü) kaydına bakın. Derleme/canlı test yapılamadı — kullanıcı doğrulamalı.

## ❌ Vazgeçildi: Playwright ile Tarayıcı Motoru Değişimi (PoC, 2026-08-15 — 2026-08-18'de vazgeçildi, 2026-08-24'te temizlendi)

- [x] Kod tabanı analiz edildi: 321 dosyanın ~28'i Selenium tiplerine doğrudan bağımlı, geri kalanı (300'den fazla `*Parser.cs`) HtmlAgilityPack ile ham HTML parse ettiği için tarayıcı motorundan habersiz — aşamalı göç mümkün olduğu gösterildi.
- [x] Kullanıcıyla karar netleşti: "big bang" değil, önce küçük bir kanıt.
- [x] Kanıt yazıldı: `PlaywrightBrowserPoc.cs` + `FoundNewVillageCommandPoc.cs` (mevcut `MapParser`/`SettleConfirmParser`'ı değiştirmeden yeniden kullanıyor) + Debug sekmesine test butonu + `Microsoft.Playwright` paket referansı. Üretim koduna (`ChromeBrowser.cs`/`IChromeBrowser`/dispatcher) hiç dokunulmadı.
- [x] 2026-08-16: İlk GitHub Actions denemesinde `DebugViewModel.cs`'de derleme hatası çıktı (`GetRequiredService` için eksik `using Microsoft.Extensions.DependencyInjection;`) — düzeltildi.
- [x] 2026-08-18: Kullanıcı bu göçü vazgeçti — Playwright'a geçiş bir daha gündeme alınmayacak.
- [x] **2026-08-24: Temizlendi.** PoC kodu (`PlaywrightBrowserPoc.cs`, `FoundNewVillageCommandPoc.cs`, klasörleri, Debug sekmesindeki panel/binding'ler, `Microsoft.Playwright` paket referansı, `DebugViewModel`'in artık gereksiz kalan `ICustomServiceScopeFactory` bağımlılığı) tamamen kaldırıldı. Ayrıntılar için CHANGELOG.md'deki 2026-08-24 kaydına bakın. Derleme/canlı test yapılamadı (`dotnet` yok) — kullanıcı doğrulamalı: Debug sekmesi sorunsuz açılıyor mu.

## ✅ Çözüldü: ZIP, Hafıza Dosyalarının Gerisindeydi (2026-08-13'te tespit edildi, 2026-08-14'te kapatıldı)

- [x] Yüklenen ZIP'te eksik olan §5k (Auto Settle) ve §5m (Dodge evrimi) kodu bu oturumda ZIP üzerine yeniden yazıldı: `AutoSettleTask.cs`/`SupplyForSettleTask.cs`/`MainCore/Commands/Features/Settle/*` (yeni) ve `DodgeTroopTask.cs`/`RecallTroopTask.cs`'in saldırı-tipi evrimi + `TroopCheckItem.cs`/`DodgeTroopSelectorViewModel.cs` (yeni); `SendReinforcementCommand.cs` silindi. Ayrıntılar için CHANGELOG.md'deki 2026-08-14 kaydına bakın.

## Yeni Özellik (Kod ZIP'e Yazıldı, Derleme/Canlı Doğrulama Bekliyor): Dodge (Kaçınma) Sistemi Saldırıya Evrildi (2026-08-13/14)

- [x] 90 saniyelik gerçek iptal penceresi web'den doğrulandı (kullanıcının bildiği 1 dk değil).
- [x] Zamanlama netleşti: varışa 30 sn kala gönderim, gönderimden 50 sn sonra iptal.
- [x] Hedef tipi saldırıya çevrildi (artık gerçek köy zorunluluğu yok).
- [x] `DodgeTroopTask`/`RecallTroopTask`/`RecallTroopCommand` tamamen yeniden yazıldı; `SendReinforcementCommand` silinip yerine SyncAttack'ın doğrulanmış `SendTroopsCommand`'ı kullanıldı.
- [x] Çoklu asker tipi seçimi (1-10 checkbox, göçmen dahil) `DodgeTroopSlotsMask` + `DodgeTroopSelectorViewModel` ile eklendi.
- [x] WPF UI (checkbox listesi, hedef X/Y, gönderim/iptal saniyeleri) `VillageSettingTab.xaml`/`.xaml.cs`'e bağlandı (2026-08-14).
- [x] `GetOutgoingAttackAbortButton` headline-metni bazlı eşleştirmeye geçirildi (eski `<th class="coords">`/kaynak-köy hatası düzeltildi) (2026-08-14).
- [ ] Gerçek Windows ortamında derlenip test edilmeli (bu ortamda `dotnet` SDK yok, sadece parantez dengesi/statik inceleme yapıldı; WPF/XAML tarafı hiç görsel olarak test edilemedi).
- [ ] 30/50 saniyelik varsayılan zamanlamanın otomasyon gecikmesine (sayfa yükleme, tıklama) karşı canlıda yeterli pay bırakıp bırakmadığı gözlemlenmeli.
- [ ] Attack-tipi bir hareketin gerçek headline metninin (`(X|Y)` formatı) canlıda `GetOutgoingAttackAbortButton`'daki varsayımla birebir eştiği doğrulanmalı — göçmen capture'ından ("Founding a new village (X|Y)") dolaylı bir emsal alındı, doğrudan bir "boş koordinata saldırı" capture'ı henüz yok.
- [x] ⚠️ **Canlı bug düzeltildi (kullanıcı bildirdi, gerçek capture ile doğrulandı, 2026-08-21):** RaidList çalışırken (veya herhangi bir giden saldırı gönderiminde) Telegram'a yanlış "saldırı altında!" bildirimi gidiyordu. Kök neden: `MovementsParser.GetIncomingAttackSeconds`, dorf1/dorf2 sidebar'ındaki `#movements` widget'ında satırların "Incoming troops:" mı "Outgoing troops:" mı bölümünde olduğuna bakmadan, sadece ikon class'ı `att*` mi diye kontrol ediyordu — ama giden yağma/saldırılar da Travian'da aynı `att*` ikonuyla gösteriliyor (yön ikon class'ında değil, hangi bölüm başlığı altında olduğunda kodlu). Düzeltme: metod artık hangi bölümde olduğunu takip edip sadece "Incoming troops:" altındaki satırları sayıyor. Kullanıcının gerçek capture'ı (Europe_11-1.html) üzerinde Python'da aynı mantık simüle edilerek doğrulandı: eski kod 166s (yanlış pozitif) döndürüyordu, yeni kod `null` döndürüyor. Ayrıntılar için CHANGELOG.md'deki 2026-08-21 (bu konudaki en son) kaydına bakın. Derleme/canlı test yapılamadı — kullanıcı Windows/CI'da derleyip RaidList aktifken artık yanlış bildirim gelmediğini doğrulamalı.

## Yeni Özellik (Kod ZIP'e Yazıldı, Derleme/Canlı Doğrulama Bekliyor): Otomatik Göçmen Eğitimi / Yeni Köy Kurma "Auto Settle" (2026-08-12/14)

- [x] Tasarım kararları netleşti: eksik kaynak hesaptaki tüm köylerden otomatik çekilir (opt-in yok); yeni köy hedefi sabit kullanıcı X/Y'si (otomatik en-yakın-boş-alan araması yok); 750'şer kuruluş maliyeti göçmen eğitimiyle aynı talep mekanizmasını kullanır.
- [x] `AutoSettleTask`, `SupplyForSettleTask`, `ToSettlerTrainPageCommand`, `ToExpansionPageCommand`, `TrainSettlerCommand`, `FoundNewVillageCommand` yazıldı; `TrainTroopParser`'a `GetPresentAmount`/`GetUnitCost` eklendi; yeni `VillageSettingEnums` ayarları + varsayılanları eklendi; `CustomServiceScopeFactory.cs` ve `UpdateStorageCommand.cs`'e entegre edildi (2026-08-14).
- [x] "Yeni köy kur" akışı kodlandı (2026-08-14): `MapParser`/`SettleConfirmParser` + `ToMapCommand`/`FoundNewVillageCommand` — haritada X/Y gir → beliren "Found new village" linkine tıkla → onay ekranında Settle'a bas, her tıklamadan sonra §2e'ye uygun post-click doğrulama var.
- [x] Yeni ayarlar (`AutoSettleEnable`, `AutoSettleTargetX/Y`, `ExpansionSupplyReservePercent`) WPF UI'a (`VillageSettingTab.xaml`/`.xaml.cs`) bağlandı (2026-08-14).
- [ ] `TrainTroopParser.GetPresentAmount`/`GetUnitCost`'un HTML yapısı (`value`/`animatedNumber`/`resourceWrapper` sınıf adları) gerçek bir Residence/Palace Train sekmesi capture'ı ile **henüz doğrulanmadı** — varsayıma dayalı yazıldı, canlıda 0 dönerse önce bu fonksiyonlar kontrol edilmeli.
- [ ] Gerçek Windows ortamında derlenip test edilmeli — bu ortamda `dotnet` SDK yok.
- [ ] `SupplyForSettleTask`'ın rezerv (%30 varsayılan) ve jitter mantığı canlı ortamda, birden fazla köyün aynı anda talepte bulunduğu senaryoda gözlemlenmeli.
- [ ] Göçmen eğitimi için `stillNeeded` hesaplanırken kalan (leftover) storage'ın 0 varsayılması (küçük bir "fazla isteme" payı bırakıyor) kabul edilebilir mi, yoksa daha hassas bir hesaplama mı gerekiyor — canlı kullanımda gözlemlenmeli.

## Canlı Bug (Düzeltildi, Gerçek Capture ile Doğrulandı): Auto Settle'da Harita Koordinat Girişi Haritayı Zıplatmıyordu (2026-08-15)

- [x] Kullanıcının paylaştığı canlı logdan teşhis edildi: `FoundNewVillageCommand`, X/Y'yi harita arama kutusuna yazıyor ama haritayı hiçbir şey zıplatmıyor, "Found new village" linki hiç belirmiyor.
- [x] İlk deneme (Enter tuşu) yanlış çıktı — kullanıcı gerçek sayfayı inceleyip ayrı bir "Git"/OK butonuna tıklanması gerektiğini doğruladı.
- [x] Kesin düzeltme yapıldı ve gerçek capture ile doğrulandı: `MapParser.GetGoButton` eklendi (buton ID'si dinamik olduğu için form içinde `type="submit"` araması yapıyor), `FoundNewVillageCommand` artık bu butona tıklıyor.
- [x] **Devamı — Git butonu tek başına yetmiyordu (2026-08-17, gerçek capture ile doğrulandı):** kullanıcı, haritanın hedefe kaydığını ama "Found new village" linkinin hâlâ belirmediğini, çünkü merkez tile'a ayrıca sol tık gerektiğini bildirdi. Kullanıcının paylaştığı tam `karte.php` HTML'i, haritada per-tile DOM elementi olmadığını (tile'lar arka plan resmi) gösterdi. `MapParser.GetMapContainer` eklendi (`#mapContainer`, sabit id), `FoundNewVillageCommand.InputCoordinates` artık Git'ten sonra X/Y value'larının hedefle eştiğini bekleyip `mapContainer`'a tıklıyor (WebDriver `Click()` zaten elementin merkezine tıklıyor, tile-özel seçiciye gerek yok). Bkz. CHANGELOG.md 2026-08-17.
- [x] **⚠️ Kendi düzeltmemdeki yanlış varsayım, canlı testte yakalandı (2026-08-17, aynı gün):** Yukarıdaki düzeltmenin `jumpedToTarget` doğrulaması, "Git tıklaması tam sayfa reload'u tetikler" varsayımına dayanıyordu — bu **doğrulanmamış bir varsayımdı**, canlı test 180sn'de hep timeout yiyerek yanlış olduğunu kanıtladı (muhtemelen Git aslında JS/AJAX ile harita kaydırıyor, sayfa reload olmuyor). Doğrulama kaldırıldı, yerine kod tabanındaki yerleşik `IDelayService.DelayClick` deseni + zaten var olan `linkAppeared` son-durum kontrolü kondu.
- [ ] Kullanıcı canlı ortamda tekrar test etmeli — bu ortamda derleme yapılamadı.

## Yeni Özellik (Kod Yazıldı, Derleme/Canlı Doğrulama Bekliyor): Wave Attack (Çoklu Dalga Kesici) (2026-08-15)

- [x] Tasarım kararları netleşti (kullanıcıyla soru-cevap): tek köyden gönderim (çoklu köy YOK); wave miktarları kullanıcının elle gireceği sabit sayılar (mevcudun tamamı DEĞİL); dalgalar arası tek bir sabit saniye aralığı (her wave için ayrı saniye YOK); ana dalgada kahraman da gönderilebilmeli.
- [x] `WaveAttackPlanner` (saf/test edilebilir, `SyncAttackPlanner`'ın tekli-köy/çoklu-dalga aynası) + `WaveAttackPlanTask` + `WaveAttackPlanDto` yazıldı; `SendTroopsCommand`/`SendTroopsAtTimeTask`'a `IncludeHero` eklendi; dispatcher case'i eklendi; WPF UI (`WaveAttackTab.xaml`/`.xaml.cs`, `WaveAttackViewModel`) `AccountTabStore`/`MainLayoutUc`'a bağlandı; `WaveAttackPlannerTest.cs` (6 test) eklendi.
- [x] Kahraman gönderim alanı (`troop[t11]`) kullanıcının paylaştığı gerçek Rally Point capture'ı ile doğrulandı — diğer 10 asker slotuyla birebir aynı markup (checkbox değil, düz input), `InputTroopAmount`'un doğrudan yeniden kullanılmasına izin verdi.
- [x] Kod incelemesi sırasında bulunup düzeltildi: `WaveAttackPlanTask`'ta ana dalga probe'undan sonra tekrar-dalgası probe'undan önce Send Troops sayfasına tekrar gidilmiyordu (sayfa Rally Point Overview'da kalıyordu) — düzeltildi.
- [ ] Gerçek Windows ortamında derlenip test edilmeli (bu ortamda `dotnet` SDK yok — sadece parantez dengesi + XAML well-formed kontrolü yapıldı, WPF tarafı hiç görsel render edilemedi).
- [ ] `WaveAttackPlanTask`'ın iki ayrı probe'u (ana dalga + tekrar dalgası) art arda aynı görev içinde canlı ortamda test edilmeli — özellikle probe sonrası Rally Point Overview'a dönüp tekrar Send Troops sayfasına gitme adımının (yeni eklenen düzeltme) gerçekten gerekli/yeterli olduğu.
- [ ] `WaveAttackPlanner.MinimumRecommendedSendGap` (5 sn) ve `SafetyBuffer` (15 sn) değerlerinin küçük/hızlı wave'ler (örn. 1 sn'lik GapSeconds) için canlıda pratikte yetip yetmediği gözlemlenmeli — kullanıcının örneğindeki "1 saniye sonra" gibi çok sık wave'lerde tarayıcının fiziksel olarak yetişip yetişmediği ilk denemede izlenmeli.
- [ ] Ana dalga ve tekrar dalgası dışında, aradaki "gerçek yıkım" (mancınıkla duvar indirme) hesaplaması bot tarafında yapılmıyor — kullanıcı miktarları/timing'i kendisi belirliyor, bot sadece zamanlamayı hassas şekilde uyguluyor. Bu bilinçli bir sınır, TODO değil, ama not düşülüyor.



- [x] Kullanıcının yüklediği `Task_goreve_gore_koy_gelisimi.tbs` incelendi: uygulamanın mevcut `BuildViewModel.Export()`/`Import()` (`JobDto`) formatıyla birebir aynı, 63 kayıt (57 NormalBuild + 6 ResourceBuild, seviye 1-20).
- [x] Tek sabit şablon kararı netleşti — hesap başına tek dosya, çoklu şablon seçimi yok.
- [x] `context.Jobs`'a satır eklemenin, mevcut `UpgradeBuildingTask` sayesinde inşayı otomatik başlatmaya yettiği doğrulandı — ayrı bir "başlat" mekanizması gerekmiyor.
- [x] Kodlandı (2026-08-15): şablon `MainCore/Resources/DefaultBuildTemplate.json` olarak koda gömüldü (embedded resource, ayrı import UI'ı yok, ayrı aç/kapa ayarı yok — kullanıcıyla netleşen karar). Yeni köy, `UpdateVillageListCommand`'ın senkronize ettiği listede ilk göründüğü an `ApplyBuildTemplateTask` kuyruğa giriyor; bu görev, o köyün binaları taranana kadar (`CanStart`) bekliyor, sonra `FixJobsCommand`'ı (mevcut Import akışıyla aynı) kullanıp şablonu uyguluyor. Dispatcher case'i eklendi.
- [ ] Gerçek Windows ortamında derlenip test edilmeli (bu ortamda `dotnet` SDK yok).
- [ ] Bilinen bağımlılık: `AccountSettingEnums.EnableAutoLoadVillageBuilding` kapalıysa yeni köyün binaları hiç taranmıyor, dolayısıyla `ApplyBuildTemplateTask` sonsuza kadar bekler kalır — bu bir bug değil ama canlıda gözlemlenmeli/hatırlanmalı.

## Bugfix (Düzeltildi, Canlı/CI Doğrulama Bekliyor): Town Hall Yükseltmesi Sonsuz Retry'a Giriyordu — Yanlış `HasMultipleTabs()` Girdisi (2026-08-11)

- [x] Kullanıcının canlı log'u teşhis edildi: "Upgrade building in 2001" (Town Hall, slot 20) art arda "Found 0 tabs but need tab #1 active" hatasıyla başarısız oluyordu; ekran görüntüsü Town Hall Level 1 sayfasında hiç tab çubuğu olmadığını gösteriyordu.
- [x] Kullanıcının paylaştığı gerçek Town Hall sayfası HTML'i incelendi: `contentNavi`/`subNavi`/`tabItem` class'larının hiçbiri sayfada geçmiyor (0 eşleşme) — sayfada gerçekten tab yok. Kök neden `BuildingEnums.HasMultipleTabs()`'ta yanlışlıkla `TownHall => true` olması, `SwitchManagementTabCommand`'ın var olmayan tab'a geçmeye çalışıp sonsuza dek retry'a girmesi.
- [x] `MainCore/Enums/BuildingEnums.cs`'de `HasMultipleTabs()`'tan `TownHall` girdisi kaldırılarak düzeltildi (2026-08-11).
- [ ] Push sonrası CI derlemesi ve canlı hesapta Town Hall yükseltmesinin artık "Upgrade to level X" butonuna gerçekten tıklayıp inşaatı kuyruğa aldığı doğrulanmalı.
- [ ] Town Hall'ın daha yüksek seviyelerde (örn. Celebrations ile ilgili bir tab gerekip gerekmediği) gerçekten tab çubuğu kazanıp kazanmadığı ayrıca gözlemlenmeli — mevcut düzeltme yalnızca Level 1'deki gerçek HTML kanıtına dayanıyor.

## Bugfix (Düzeltildi, Canlı/CI Doğrulama Bekliyor): Smithy "Improve" Butonu Bulunamıyordu — HtmlAgilityPack Entity Decode Sorunu (2026-08-10)

- [x] Kullanıcının canlı log'u teşhis edildi: `Tribe` artık doğru geliyor (Gauls), research bloğu bulunuyor (`IsResearchBlockMissing` tetiklenmiyor), ama "Improve" butonu hep `null` dönüp "not available right now" veriyordu — kullanıcının paylaştığı ekran görüntüsü ve gerçek sayfa HTML'i butonun oyun tarafında mevcut/tıklanabilir olduğunu gösteriyordu.
- [x] Kullanıcının paylaştığı gerçek buton HTML'i incelenip kök neden bulundu: `GetImproveButton`'daki `&t=t{slot}&` deseni, HtmlAgilityPack'in decode etmediği, sayfa kaynağındaki ham `&amp;`-kodlu `onclick` metniyle hiçbir zaman eşleşmiyordu.
- [x] `MainCore/Parsers/SmithyParser.cs`'de karşılaştırmadan önce `HtmlEntity.DeEntitize(...)` eklenerek düzeltildi (2026-08-10); kullanıcının gerçek sayfa HTML'i üzerinde Python ile simülasyonla doğrulandı (eski desen eşleşmiyor, yeni desen eşleşiyor).
- [ ] Push sonrası CI derlemesi ve canlı hesapta Smithy upgrade'in artık "Improve" butonuna gerçekten tıklayıp research'ü başlattığı doğrulanmalı.

## Bugfix (Düzeltildi, Canlı Doğrulama Bekliyor): Sync Attack Canlıda Çalışmıyor — Dispatcher'da Eksik Case (2026-08-10)

- [x] Kullanıcının paylaştığı canlı log teşhis edildi: `System.NotImplementedException: Task Task is not implemented`, `CustomServiceScopeExtension.Execute`'taki merkezi `switch`'te `SyncAttackPlanTask.Task`/`SendTroopsAtTimeTask.Task` için case bulunmadığından kaynaklanıyor (2026-08-09'daki dal taşımasında bu dosya güncellenmemiş) — bkz. `PROJECT_CONTEXT.md` §5j, `CHANGELOG.md`.
- [x] `CustomServiceScopeFactory.cs`'e iki case eklendi (`SyncAttackPlanTask.Task`, `SendTroopsAtTimeTask.Task`), mevcut 19 case ile aynı kalıpta (2026-08-10).
- [ ] Bu iki `[Handler]` partial class'ının DI kaydının (Injectio) eksiksiz olduğu teyit edilmeli — `HostingTest.cs`'in bunu yakalamadığı doğrulandı (host testi sadece DI grafiğini build ediyor, elle yazılmış dispatcher switch'ini hiç çağırmıyor).
- [ ] Push sonrası CI derlemesi ve canlı hesapta "Synchronized attack - calculate & schedule" task'ının artık `NotImplementedException` fırlatmadan çalıştığı doğrulanmalı.
- [ ] Kullanıcının paylaştığı 5 canlı HTML sayfası (overview, rally point, send troops, send troops onay, köy içi) henüz detaylı incelenmedi — dispatcher düzeltmesinden sonra asker gönderme akışının parse mantığını bu HTML'lere karşı ayrıca doğrulamak gerekebilir.

## Analiz Sonrası Açık Sorular / Doğrulanacaklar

- [ ] Bu "NicLay" varyantının upstream TravianBotSharp'tan somut farkları var mı? (Yüklenen zip'te `.git` geçmişi yok, karşılaştırma yapılamadı.)
- [ ] `.github/instructions/copilot-commit.instructions.md` içeriği henüz detaylı okunmadı — commit atmadan önce oku ve `CLAUDE.md`'ye özet ekle.
- [ ] `.understand-anything/knowledge-graph.json` ve `domain-graph.json` içerikleri henüz derinlemesine incelenmedi — büyük bir refactor öncesi bakılmalı.
- [ ] Kullanıcının "bu proje daha önce seninle geliştirildi" dediği önceki oturum/geçmiş bu ortamda mevcut değil — kullanıcıdan önceki oturumda neyle uğraşıldığına dair özet istenebilir.

## Kullanıcıdan Beklenen Girdi

- [ ] "Auto Settle" özelliğini tamamlamak için: 3 göçmen hazırken Residence/Palace'ın **Expansion** sekmesinin (ve varsa onay ekranının) gerçek HTML'i (kullanıcı en kısa sürede göndereceğini belirtti, 2026-08-12).
- [ ] Dodge sistemi ilk canlı denemede iptal (X) butonu bulunamazsa: bir saldırı hareketi yoldayken Rally Point Overview'ın o anki HTML'i (2026-08-13).

## Bilinen Teknik Notlar (ileride referans için)

- [ ] `Infrasturecture` klasör adındaki yazım hatası bilinçli olarak korunuyor — düzeltme talebi gelirse önce build/namespace etkisini kullanıcıya hatırlat.
- [ ] Proje yalnızca Windows'ta çalışıyor (WPF + win-x64 RID) — Linux/macOS'ta derlenemez, bu ortamda (sandbox) build/test çalıştırılamaz, sadece statik analiz/kod düzenleme yapılabilir.
- [ ] GitHub Actions workflow'larındaki `actions/checkout@v4` / `actions/setup-dotnet@v4` Node.js 20 deprecation uyarısı henüz giderilmedi (build'i kırmıyor ama ileride sorun çıkarabilir) — `@v5` gibi güncel bir sürüme geçmek gerekebilir, `.github/workflows/*.yml` incelenmeli.
- [ ] `MainCore/Tasks/` altındaki `[Handler]` static partial class'larda iç içe `Task` adlı sınıf tanımlama kalıbı (`SmithyUpgradeTask.Task`, `SmallCelebrationTask.Task` gibi) — yeni bir Task dosyası eklerken aynı hataya (`System.Threading.Tasks.Task` ile isim çakışması) düşmemek için `async Task` yerine `async System.Threading.Tasks.Task` yazma alışkanlığı `CLAUDE.md`'ye eklenmeli.
- [ ] EF Core `IQueryable` sorgularında (`context.Villages.Where(...)` gibi expression tree'ye derlenen lambda'larda) `is null` / `is not null` pattern-matching kullanılmamalı — `== null` / `!= null` / `.HasValue` tercih edilmeli. Bu kural `CLAUDE.md`'ye eklenmeli (bkz. `SupplyFromHammerTask.cs` düzeltmesi, 2026-08-04).
- [ ] GitHub Actions'ta gerçek `dotnet build` ile doğrulama yapılmadı (bu ortamda mümkün değil) — kullanıcı push edip CI sonucunu paylaşmalı.

## Takip Düzeltmesi: Asıl Kök Neden `AccountsInfo.Tribe`'ın Hep "Any" Olması (2026-08-09) — Kullanıcı Ön Koşulu Var, Canlı Doğrulama Bekliyor

- [ ] **Kullanıcı önce şunu doğrulamalı:** Account Setting sekmesinde Tribe alanı elle Gauls (veya hesabın gerçek kabilesi) olarak seçilip kaydedilmiş mi? `AccountSettingEnums.Tribe`'ın varsayılanı da `0` (`Any`) olduğu için, bu seçilmemişse düzeltme yine işe yaramaz ve aynı "research block not found" hatası tekrarlanır.
- [ ] Yukarıdaki ön koşul sağlandıktan sonra `SmithyUpgradeTask`/`SmithyUpgradeCommand`/`SmithyParser` zinciri canlı hesapta tekrar denenmeli: kaynak yetersiz slot için artık pause olmadan sessizce beklediği doğrulanmalı.
- [ ] `SyncAttackViewModel`'de aynı düzeltme (tribe artık `AccountSettingEnums.Tribe`'dan okunuyor) sonrası köy satırlarının asker slotlarının (Slots) da doğru geldiği görsel olarak kontrol edilmeli — bir önceki oturumda sadece "köyler hiç görünmüyor" (thread) sorunu düzeltilmişti, slotların boş gelme ihtimali (tribe=Any yüzünden) o sırada fark edilmemişti.
- [ ] `VillageSettingEnums.Tribe` (her köyde `AccountSettingEnums.Tribe`'dan otomatik kopyalanan ayar) yerine doğrudan `AccountSettingEnums.Tribe` kullanmanın (mevcut yaklaşım) doğru tercih olup olmadığı teyit edilmeli — ikisi teorik olarak hep aynı değeri taşımalı (`FillAccountSettings` ile senkronize), ama bu senkronizasyonun her zaman güncel olduğu varsayımı doğrulanmadı.
- [x] "Research block for troop slot 1 not found" hatasının bir önceki düzeltmeden (2026-08-09, `AccountsInfo.Tribe` kullanan versiyon) SONRA da aynen tekrarlandığı kullanıcının paylaştığı yeni log ile teyit edildi; kök neden `UpdateAccountInfoCommand`'ın `Tribe`'ı hep `TribeEnums.Any` olarak sabitlediği bulundu, kaynak `AccountSettingEnums.Tribe`'a değiştirildi (2026-08-09).
- [x] `CLAUDE.md`'ye "AccountsInfo.Tribe'a güvenme, AccountSettingEnums.Tribe kullan" kalıcı kuralı eklendi (§2h, 2026-08-09).

## Bugfix: Smithy Kaynak Yetersizken Pause Sorunu + Sync Attack Köy Listesi (2026-08-09) — Canlı Doğrulama Bekliyor

- [x] `SmithyUpgradeCommand`/`SmithyParser` düzeltmesi (Improve butonu yerine global troop ID ile blok bulma) kod seviyesinde tamamlandı — ancak tribe kaynağı yanlış olduğu için (bkz. yukarıdaki "Takip Düzeltmesi" bölümü) tek başına yeterli olmadığı ortaya çıktı.
- [ ] Asker binası (Barracks/Stable/Workshop) hiç kurulmamış bir troopSlot için `IsResearchBlockMissing` davranışı canlı ortamda gözlemlenmeli — hâlâ `Stop.Error` tetikliyorsa bunun gerçekten istenen davranış mı olduğu (yoksa bu da normal bir durum olarak mı ele alınmalı) kullanıcıyla netleştirilmeli.
- [ ] `TroopEnums` int değerlerinin Travian'ın global troop ID'leriyle eşleştiği varsayımı sadece Gauls (Phalanx=21, Pathfinder=23, Druidrider=25) için gerçek HTML'le doğrulandı — diğer kabileler (Romans/Teutons/Egyptians/Huns) için de doğru olduğu varsayılıyor ama teyit edilmedi.
- [ ] `SyncAttackViewModel` düzeltmesi (köy listesinin artık UI thread'inde dolduğu) canlı ortamda WPF üzerinde görsel olarak doğrulanmalı — bu ortamda WPF render edilemiyor.
- [x] Kök nedenler (Smithy: kaynak-yetersiz durumu Improve butonu yokluğuyla research-bloğu-yok durumuyla karıştırılıyordu; Sync Attack: `Load()` arka plan thread'inde UI koleksiyonu değiştiriyordu) kullanıcının paylaştığı gerçek log + HTML ile teyit edildi ve düzeltildi (2026-08-09).

## Yeni Özellik: Çoklu Köyden Senkronize Varış (2026-08-09, bu dala taşındı) — CI Doğrulaması Bekliyor

- [ ] `RallyPointTroopSlots.GetSlots(TribeEnums)` tablosu canlı bir hesapta (özellikle Galya) teyit edilmeli — slot sırası `TroopEnums` bildirim sırasından çıkarıldı, gerçek sayfa capture'ıyla doğrulanmadı.
- [ ] Gerçek Windows ortamında derlenip test edilmeli — bu ortamda `dotnet` SDK yok, sadece elle statik inceleme yapıldı.
- [ ] Prova (dry-run) akışı canlı bir hesapta uçtan uca denenmeli: onay ekranına gidip onaylamadan Rally Point overview'a geri dönmenin askerleri gerçekten etkilemediği doğrulanmalı.
- [ ] Taşıma sırasında `SendTroopsCommand`'a eklenen Confirm-sonrası doğrulama (`!IsConfirmPage`) canlı hesapta denenmeli — özellikle gerçek başarılı bir gönderimde bu kontrolün yanlışlıkla `Stop.Error` döndürmediğinden (ör. onay sonrası yönlendirilen sayfa beklenenden farklıysa) emin olunmalı.
- [ ] İki kaynak köyün gönderme zamanı çok yakınsa (`SyncAttackPlanner.SafetyBuffer` = 15sn'den az farkla) gerçek sapma miktarı canlı ortamda ölçülmeli.
- [ ] "Sync attack" sekmesinin WPF tasarımcıda düzgün render olduğu görsel olarak kontrol edilmeli.
- [x] Özellik, ayrı bir dalda (08-05 tabanlı) geliştirilmişti; bu kod tabanına (08-07 tabanlı, smithy-fix + prerequisite-resolve içeren) elle taşındı, bağımlılıklar `diff` ile tek tek doğrulandı (2026-08-09).

## Çözülen Konu: Smithy Zırh Yükseltmesi Onaylanmıyor (2026-08-07) — Canlı Doğrulama Bekliyor

- [x] Kullanıcının paylaştığı gerçek HTML incelendi; seçici mantığı doğru çıktı, gerçek sorun tıklama-sonrası doğrulama eksikliğiydi (`WaitPageChanged` çağrılmıyordu) — bkz. CHANGELOG.md.
- [ ] Düzeltme (`WaitPageChanged("action=research")` + `GetOngoingResearchSecondsRemaining` kontrolü) canlı hesapta denenmeli. Sorun devam ederse artık `Stop.Error` ile görünür olacak — hata mesajı teşhisi kolaylaştıracak.

## Yeni Özellik: Eksik Bina Ön Koşulunu Otomatik Çözme (2026-08-07) — CI Doğrulaması Bekliyor

- [ ] `ResolveMissingPrerequisiteCommand` gerçek Windows ortamında derlenip test edilmeli — bu ortamda `dotnet` SDK yok, sadece elle statik inceleme + saf mantık için unit testler yazıldı (DB/browser gerektirmeyen `GetPrerequisitePlan` fonksiyonu).
- [ ] Art arda birden fazla eksik ön koşulun (ör. Bakery → GrainMill → Cropland zinciri) tek bir bot cycle'ında doğru sırayla çözüldüğü canlı bir hesapta gözlemlenmeli.
- [ ] İnşaat kuyruğu (aynı anda max 2-3 iş) doluyken bu mekanizmanın davranışı canlı ortamda izlenmeli.
- [ ] `maxPrerequisiteResolutions = 8` sabiti gerçek kullanımda yeterli mi (çok derin bina zincirleri için) gözlemlenmeli, gerekirse ayarlanabilir bir ayara taşınabilir.
- [x] Boş arsa yoksa davranış (Skip+log), job kuyruk sırası (ToTop) ve güvenlik sınırı ihtiyacı kullanıcıyla netleştirildi (2026-08-07).

## Yeni Özellik: Kahraman Sağlığına Göre Macera Kısıtı (2026-08-05) — CI Doğrulaması Bekliyor

- [ ] `MinHeroHealthPercent` özelliği gerçek Windows ortamında derlenip test edilmeli — bu ortamda `dotnet` SDK yok, sadece elle statik inceleme yapıldı.
- [ ] `hero/attributes` sayfasına navigate + health% okuma akışı canlı bir hesapta uçtan uca denenmeli (bu ortamda Selenium/tarayıcı çalıştırılamıyor).
- [ ] Sağlık eşiği düşükken görevin `Skip` ile kuyruktan düşüp düşmediği, sonra `EnableAutoStartAdventure` açık kaldığı sürece otomatik olarak tekrar kuyruğa alınıp alınmadığı canlı ortamda gözlemlenmeli (mevcut "macera yok" skip akışıyla aynı mekanizmayı kullanıyor, ama doğrulanmalı).
- [ ] Account Setting Tab'daki yeni "Don't send hero below this health %" alanının WPF tasarımcıda düzgün render olduğu görsel olarak kontrol edilmeli.

## Yeni Özellik: Online Saatleri (2026-08-04) — CI Doğrulaması Bekliyor

- [ ] `online-hours-feature.patch` / `TravianBotSharp-online-hours.zip` kullanıcı tarafından gerçek Windows ortamında derlenip test edilmeli — bu ortamda `dotnet` SDK yok, sadece elle statik inceleme yapıldı.
- [x] Kullanıcıya patch/zip'in kendi repo'suna nasıl uygulanacağı anlatıldı (`git apply online-hours-feature.patch` ya da dosya dosya elle kopyalama, 13 dosyalık liste ile) (2026-08-04).
- [ ] Account Setting Tab'daki yeni "Online hours" bölümünün WPF tasarımcıda (XAML) düzgün render olduğu görsel olarak kontrol edilmeli (bu ortamda WPF render edilemez).
- [ ] Kullanıcı isterse "Allowed hours" etiketini / saatlerin UTC mi yoksa yerel PC saati mi olduğunu netleştirmek isteyebilir — şu an `DateTime.Now.Hour` (yerel PC saati) kullanılıyor, bu doğrulanmalı/belgelenmeli.

## Tamamlanan

- [x] Proje zip'i açıldı ve tam yapı analiz edildi (2026-08-04).
- [x] `PROJECT_CONTEXT.md`, `CLAUDE.md`, `TODO.md`, `CHANGELOG.md` oluşturuldu (2026-08-04).
- [x] GitHub Actions build hatası (`Task` isim çakışması) düzeltildi: `SmithyUpgradeTask.cs`, `SmallCelebrationTask.cs` (2026-08-04).
- [x] GitHub Actions build hatası (expression tree içinde `is null`) düzeltildi: `SupplyFromHammerTask.cs` (2026-08-04).
- [x] Kullanıcının güncel GitHub zip'inde her iki düzeltme de doğrulandı (2026-08-04).
- [x] Yeni özellik: `OnlineHoursMask` hesap ayarı + `HoursScheduleUc` (24 saatlik checkbox) + `TimerManager` gating (2026-08-04) — bkz. CHANGELOG.md.
- [x] Yeni özellik: `MinHeroHealthPercent` hesap ayarı + `HeroParser`/`CheckHeroHealthCommand` (hero/attributes sayfasından sağlık okuma) + `StartAdventureTask` gating (2026-08-05) — bkz. CHANGELOG.md.
- [x] Yeni özellik: Eksik bina ön koşulunu (`ResolveMissingPrerequisiteCommand`) otomatik çözme — mevcut binayı yükseltme veya boş arsaya yeni bina kurma, `GetBuildPlanCommand` içinde zincirleme (recursive) çalışan mekanizma (2026-08-07) — bkz. CHANGELOG.md.
- [x] Bugfix: Smithy zırh yükseltmesi tıklanıyor ama onaylanmıyor — `SmithyUpgradeCommand`'a eksik tıklama-sonrası doğrulama (`WaitPageChanged` + research kontrolü) eklendi (2026-08-07) — bkz. CHANGELOG.md.
- [x] Çoklu köyden senkronize varış özelliği (ayrı bir dalda geliştirilmişti) bu koda taşındı ve `SendTroopsCommand`'a Confirm-sonrası doğrulama eklendi (2026-08-09) — bkz. CHANGELOG.md.
