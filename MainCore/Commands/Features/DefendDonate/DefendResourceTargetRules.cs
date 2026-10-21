namespace MainCore.Commands.Features.DefendDonate
{
    // 2026-10-19: amounts of the four resources (a stock, a shipment, ...).
    public readonly record struct DefendResources(long Wood, long Clay, long Iron, long Crop)
    {
        public long Total => Wood + Clay + Iron + Crop;
    }

    // One possible receiving village, reduced to the facts the choice needs so the rule stays a
    // pure function (no EF/browser) and can be unit tested. Warehouse = wood/clay/iron capacity,
    // Granary = crop capacity (CLAUDE.md 2j: they are CAPACITIES, not stock).
    public readonly record struct DefendTargetCandidate(int VillageId, DefendResources Stock, long Warehouse, long Granary, bool IsUnderAttack);

    // Pure rules for DefendDonateTask's "send everything away" step (user decision 2026-10-19:
    // pick any own village at random, but prefer one whose storage has room).
    public static class DefendResourceTargetRules
    {
        // Same proportional scale-down SendResourceCommand applies when the merchants cannot
        // carry the whole plan - done up front so the "does it fit" check sees the real shipment.
        public static DefendResources ScaleToCapacity(DefendResources amounts, long maxTotal)
        {
            var total = amounts.Total;
            if (total <= 0 || total <= maxTotal) return amounts;
            if (maxTotal <= 0) return new DefendResources(0, 0, 0, 0);

            return new DefendResources(
                amounts.Wood * maxTotal / total,
                amounts.Clay * maxTotal / total,
                amounts.Iron * maxTotal / total,
                amounts.Crop * maxTotal / total);
        }

        // Share (0..1) of the shipment the candidate's storage can still take, resource by resource.
        public static double FitFraction(DefendTargetCandidate candidate, DefendResources send)
        {
            var total = send.Total;
            if (total <= 0) return 0;

            var fitting =
                Math.Min(send.Wood, Room(candidate.Warehouse, candidate.Stock.Wood)) +
                Math.Min(send.Clay, Room(candidate.Warehouse, candidate.Stock.Clay)) +
                Math.Min(send.Iron, Room(candidate.Warehouse, candidate.Stock.Iron)) +
                Math.Min(send.Crop, Room(candidate.Granary, candidate.Stock.Crop));

            return (double)fitting / total;
        }

        // Rule: villages under attack are skipped (their stock may be looted too) unless nothing
        // else exists. Among the rest, a village that can take the WHOLE shipment is picked at
        // random; if none can, the one(s) with the best fit (ties random). Null = no candidate.
        public static int? ChooseTarget(IReadOnlyList<DefendTargetCandidate> candidates, DefendResources send, Random random)
        {
            if (candidates.Count == 0) return null;

            var pool = candidates.Where(c => !c.IsUnderAttack).ToList();
            if (pool.Count == 0) pool = candidates.ToList();

            var fits = pool.Select(c => (Candidate: c, Fit: FitFraction(c, send))).ToList();
            var best = fits.Max(x => x.Fit);

            var top = best >= 1.0 - 1e-9
                ? fits.Where(x => x.Fit >= 1.0 - 1e-9).ToList()
                : fits.Where(x => x.Fit >= best - 1e-9).ToList();

            return top[random.Next(top.Count)].Candidate.VillageId;
        }

        private static long Room(long capacity, long stock) => Math.Max(0, capacity - stock);
    }
}
