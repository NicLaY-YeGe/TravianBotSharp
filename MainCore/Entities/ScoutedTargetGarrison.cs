using System.Text.Json;

#nullable disable

namespace MainCore.Entities
{
    // 2026-09-27, user request ("raporlara göre optimize yağma otomasyonu... karşı tarafın
    // askerlerini gördüğü kadarıyla kabaca kayıp kazanç hesabı yapacak"): what the most recent
    // report told us about the garrison at one map coordinate, ACCOUNT-WIDE - not tied to a
    // specific RaidListEntry row or Sync Attack plan, since the same coordinate can be a target
    // of either (see CombatDecisionRules). Written by RaidReportTask whenever it parses a report
    // whose defender troops were actually revealed (see ReportTroopTableParser); read by
    // RaidListTask (and, in a later stage, the real-Attack tasks) before a send, only when the
    // relevant AccountSettingEnums.EnableCombatCheckOn* switch is on. A NEW TABLE, so it needs
    // the hand-written CREATE TABLE patch in AppDbContext (this project has no EF Core
    // migrations - see AppDbContext's "schema patches" region and RaidListEntry's own comment
    // for the same situation).
    public class ScoutedTargetGarrison
    {
        public int Id { get; set; }

        public int AccountId { get; set; }

        public int X { get; set; }
        public int Y { get; set; }

        // JSON-serialized troop composition - see GetTroops/SetTroops. An empty list ("[]") is a
        // real, useful value: "scouted, target had zero troops" - distinct from no row existing
        // at all for this coordinate ("never scouted, or too stale to trust" - see
        // AccountSettingEnums.CombatCheckMaxAgeHours).
        public string TroopsJson { get; set; }

        public DateTime CapturedAt { get; set; }

        public IReadOnlyList<(TroopEnums Troop, int Count)> GetTroops()
        {
            if (string.IsNullOrWhiteSpace(TroopsJson)) return Array.Empty<(TroopEnums, int)>();

            try
            {
                var dtos = JsonSerializer.Deserialize<List<TroopCountDto>>(TroopsJson);
                if (dtos is null) return Array.Empty<(TroopEnums, int)>();
                return dtos.Select(x => (x.Troop, x.Count)).ToList();
            }
            catch (JsonException)
            {
                // Cached garrison estimate only - a corrupt value must never break sending.
                return Array.Empty<(TroopEnums, int)>();
            }
        }

        public void SetTroops(IReadOnlyList<(TroopEnums Troop, int Count)> troops)
        {
            var dtos = troops.Select(x => new TroopCountDto(x.Troop, x.Count)).ToList();
            TroopsJson = JsonSerializer.Serialize(dtos);
        }

        private sealed record TroopCountDto(TroopEnums Troop, int Count);
    }
}
