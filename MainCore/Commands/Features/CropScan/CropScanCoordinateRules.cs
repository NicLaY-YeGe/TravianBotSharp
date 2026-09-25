namespace MainCore.Commands.Features.CropScan
{
    // Pure coordinate math for the crop-oasis scan feature (2026-09-24, user request): given a
    // center point and a max distance (the same Euclidean "Distance: X fields" the game itself
    // shows in the tile dialog - see CropOasisTileParser.GetDistance, confirmed against a real
    // capture: dx=15,dy=1 -> sqrt(226)=15.03), returns every integer coordinate within that
    // radius (center itself excluded), NEAREST-FIRST - CropOasisScanTask works outward from the
    // center one tile per run, exactly as the user asked ("merkezden uzaklaşarak tek tek").
    //
    // No village/occupied-oasis filtering here - this only knows geometry. What's actually AT
    // each coordinate (abandoned valley vs. player village vs. occupied oasis) is unknown until
    // the tile is opened and read by CropOasisTileParser; the task filters there, tile by tile.
    public static class CropScanCoordinateRules
    {
        public static IReadOnlyList<(int X, int Y, double Distance)> GetCoordinatesByDistance(int centerX, int centerY, int maxDistance)
        {
            if (maxDistance <= 0) return Array.Empty<(int, int, double)>();

            var result = new List<(int X, int Y, double Distance)>();
            var maxDistanceSquared = (double)maxDistance * maxDistance;

            for (var dx = -maxDistance; dx <= maxDistance; dx++)
            {
                for (var dy = -maxDistance; dy <= maxDistance; dy++)
                {
                    if (dx == 0 && dy == 0) continue;

                    var distanceSquared = ((double)dx * dx) + ((double)dy * dy);
                    if (distanceSquared > maxDistanceSquared) continue;

                    result.Add((centerX + dx, centerY + dy, Math.Sqrt(distanceSquared)));
                }
            }

            return result.OrderBy(c => c.Distance).ToList();
        }
    }
}
