using HtmlAgilityPack;
using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    // Fixture: Parsers/SendResource/MarketplaceSendTab.html - a REAL capture of the Marketplace
    // "Send resources" tab (English client, 2026-10-25, 7/7 merchants free, 2250 per merchant, one
    // merchant's worth of wood typed into the form). The numbers carry invisible direction marks
    // (U+202D / U+202C) around the digits.
    public class SendResourceParserTest
    {
        private static HtmlDocument Load()
        {
            var doc = new HtmlDocument();
            doc.Load("Parsers/SendResource/MarketplaceSendTab.html");
            return doc;
        }

        [Fact]
        public void RealCapture_FreeMerchantsAndCapacityAreRead()
        {
            var doc = Load();
            SendResourceParser.HasMerchantInfo(doc).ShouldBeTrue();
            SendResourceParser.GetFreeMerchants(doc).ShouldBe(7);
            SendResourceParser.GetMerchantCapacity(doc).ShouldBe(2250);
        }

        [Fact]
        public void PageWithoutTheMerchantLine_IsNotReadYet()
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<div class=\"deliveriesOverview\"></div>");
            SendResourceParser.HasMerchantInfo(doc).ShouldBeFalse();
            SendResourceParser.GetFreeMerchants(doc).ShouldBe(0);
        }
    }
}
