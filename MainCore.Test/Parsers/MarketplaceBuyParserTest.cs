using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    // Fixtures are cut from two real captures (TS12, Marketplace level 10, 2026-11-10): Before = the Buy tab with
    // 6 of 10 merchants home, After = the same tab right after the first offer (3,900 crop for 3,500 clay) was
    // accepted (2 of 10 merchants home; the offers that now need more merchants than are free are greyed out).
    public class MarketplaceBuyParserTest : BaseParser
    {
        private const string Before = "Parsers/MarketplaceBuy/Before.html";
        private const string After = "Parsers/MarketplaceBuy/After.html";

        [Theory]
        [InlineData(Before, 6)]
        [InlineData(After, 2)]
        public void GetFreeMerchants_ReadsTheFirstNumberOfTheFraction(string path, int expected)
        {
            _html.Load(path);
            MarketplaceBuyParser.GetFreeMerchants(_html).ShouldBe(expected);
        }

        [Fact]
        public void GetMerchantCapacity_Reads975()
        {
            _html.Load(Before);
            MarketplaceBuyParser.GetMerchantCapacity(_html).ShouldBe(975);
        }

        [Fact]
        public void GetBuyTab_FindsTheTabByItsLink()
        {
            _html.Load(Before);
            var tab = MarketplaceBuyParser.GetBuyTab(_html);
            tab.ShouldNotBeNull();
            tab!.InnerText.ShouldContain("Buy");
        }

        [Fact]
        public void Filters_CropSearchAndRatioAreActive()
        {
            _html.Load(Before);
            MarketplaceBuyParser.IsCropSearchActive(_html).ShouldBe(true);
            MarketplaceBuyParser.IsOnlyCropSearched(_html).ShouldBe(true);
            MarketplaceBuyParser.IsRatioFilterActive(_html).ShouldBe(true);
        }

        [Fact]
        public void Pagination_FirstOfSevenPages_ForwardEnabled()
        {
            _html.Load(Before);
            var pagination = MarketplaceBuyParser.GetPagination(_html);
            pagination.ShouldNotBeNull();
            pagination!.Value.Selected.ShouldBe(1);
            pagination.Value.Count.ShouldBe(7);
            MarketplaceBuyParser.IsForwardEnabled(_html).ShouldBeTrue();
        }

        [Fact]
        public void GetOffers_Before_ReadsTenCropOffers()
        {
            _html.Load(Before);
            var offers = MarketplaceBuyParser.GetOffers(_html);

            offers.Count.ShouldBe(10);
            offers.All(x => x.OfferedType == "crop").ShouldBeTrue();
            offers.All(x => x.Enabled).ShouldBeTrue();

            var first = offers[0];
            first.RowIndex.ShouldBe(1);
            first.OfferedAmount.ShouldBe(3900);
            first.WantedType.ShouldBe("clay");
            first.WantedAmount.ShouldBe(3500);
            first.Merchants.ShouldBe(4);
            first.Duration.ShouldBe(new TimeSpan(1, 35, 1));

            var iron = offers[1];
            iron.OfferedAmount.ShouldBe(2400);
            iron.WantedType.ShouldBe("iron");
            iron.Merchants.ShouldBe(3);
            iron.Duration.ShouldBe(new TimeSpan(6, 22, 57));

            offers[9].OfferedAmount.ShouldBe(1200);
            offers[9].WantedType.ShouldBe("clay");
            offers[9].Merchants.ShouldBe(2);
        }

        [Fact]
        public void GetOffers_After_MarksOffersNeedingMoreMerchantsThanFreeAsDisabled()
        {
            _html.Load(After);
            var offers = MarketplaceBuyParser.GetOffers(_html);

            offers.Count.ShouldBe(10);
            // the accepted offer is gone, the four 2,400-for-iron offers need 3 merchants but only 2 are free
            offers.Any(x => x.OfferedAmount == 3900).ShouldBeFalse();
            var greyedOut = offers.Where(x => !x.Enabled).ToList();
            greyedOut.Count.ShouldBe(4);
            greyedOut.All(x => x.MerchantsMissing && !x.ResourceMissing && x.OfferedAmount == 2400).ShouldBeTrue();
            offers.Where(x => x.Enabled).All(x => x.OfferedAmount == 1200 && x.Merchants == 2).ShouldBeTrue();
        }

        [Fact]
        public void Signature_IsTheSameForIdenticalOffersAndDiffersOtherwise()
        {
            _html.Load(Before);
            var offers = MarketplaceBuyParser.GetOffers(_html);
            offers[1].Signature.ShouldBe(offers[2].Signature);
            offers[1].Signature.ShouldNotBe(offers[5].Signature);
        }

        [Fact]
        public void PageWithoutTheBuyTab_ReturnsEmptyResults()
        {
            _html.LoadHtml("<html><body><div id=\"content\">nothing here</div></body></html>");
            MarketplaceBuyParser.GetContainer(_html).ShouldBeNull();
            MarketplaceBuyParser.GetOffers(_html).Count.ShouldBe(0);
            MarketplaceBuyParser.GetFreeMerchants(_html).ShouldBeNull();
            MarketplaceBuyParser.GetBuyTab(_html).ShouldBeNull();
            MarketplaceBuyParser.GetPagination(_html).ShouldBeNull();
            MarketplaceBuyParser.IsForwardEnabled(_html).ShouldBeFalse();
        }
    }
}
