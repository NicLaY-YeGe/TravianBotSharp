namespace MainCore.Parsers
{
    // 2026-11-10: one row of the Marketplace "Buy" tab (build.php?...&gid=17&t=1). A plain record without any
    // HtmlAgilityPack dependency so BuyCropRules stays pure and unit testable.
    //   OfferedType / WantedType: "wood", "clay", "iron" or "crop" (the page says "lumber" for wood).
    //   RowIndex: 1-based position of the row in the table body (only valid for the snapshot it came from).
    //   Enabled: the Accept button exists and has no "disabled" CSS class (the game greys the button out when the
    //            village cannot pay or has too few free merchants - it is a class, not the disabled attribute).
    //   Merchants: how many of OUR merchants the payment needs.
    public sealed record MarketplaceOffer(
        int RowIndex,
        string OfferedType,
        long OfferedAmount,
        string WantedType,
        long WantedAmount,
        int Merchants,
        TimeSpan Duration,
        bool Enabled,
        bool ResourceMissing,
        bool MerchantsMissing,
        string Player)
    {
        // Identifies the offer independent of its row, to make sure the row we click is still the offer we chose
        // (the list re-renders and another player may take an offer first).
        public string Signature => $"{OfferedType}:{OfferedAmount}>{WantedType}:{WantedAmount}|{Merchants}|{(int)Duration.TotalSeconds}|{Player}";

        public override string ToString() => $"{OfferedAmount} {OfferedType} for {WantedAmount} {WantedType}, {Merchants} merchant(s), {Duration:hh\\:mm\\:ss} away, from {Player}";
    }
}
