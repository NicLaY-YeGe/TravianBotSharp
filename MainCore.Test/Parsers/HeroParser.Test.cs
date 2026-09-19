namespace MainCore.Test.Parsers
{
    public class HeroParser : BaseParser
    {
        private const string HeroAttributesPage = "Parsers/Hero/HeroAttributesPage.html";
        // 2026-09-18: real capture showing the alternate #content class this game version/route
        // uses ("hero_inventoryAttributes" instead of "heroV2Attributes") - see HeroParser.cs's
        // IsAttributesPage comment. Same internal structure as HeroAttributesPage.html, only the
        // #content class differs, since that's the only difference actually confirmed live.
        private const string HeroAttributesPageAltClass = "Parsers/Hero/HeroAttributesPage_hero_inventoryAttributes.html";
        private const string NotAttributesPage = "Parsers/Adventures/AdventuresPage.html";

        [Theory]
        [InlineData(HeroAttributesPage, true)]
        [InlineData(HeroAttributesPageAltClass, true)]
        [InlineData(NotAttributesPage, false)]
        public void IsAttributesPage(string path, bool expected)
        {
            _html.Load(path);
            var actual = MainCore.Parsers.HeroParser.IsAttributesPage(_html);
            actual.ShouldBe(expected);
        }

        [Fact]
        public void GetHealthPercent()
        {
            _html.Load(HeroAttributesPage);
            var actual = MainCore.Parsers.HeroParser.GetHealthPercent(_html);
            actual.ShouldBe(78);
        }

        [Fact]
        public void GetHealthPercent_NotAttributesPage_ReturnsNull()
        {
            _html.Load(NotAttributesPage);
            var actual = MainCore.Parsers.HeroParser.GetHealthPercent(_html);
            actual.ShouldBeNull();
        }
    }
}
