namespace MainCore.Test.Parsers
{
    public class BuildingTabParser : BaseParser
    {
        private const string HeroPage = "Parsers/BuildingTab/HeroPage.html";
        private const string Marketplace = "Parsers/BuildingTab/Marketplace.html";
        private const string NewBuilding = "Parsers/BuildingTab/NewBuilding.html";
        private const string Rallypoint = "Parsers/BuildingTab/Rallypoint.html";

        [Theory]
        [InlineData(HeroPage)]
        [InlineData(Marketplace)]
        [InlineData(NewBuilding)]
        [InlineData(Rallypoint)]
        public void GetNavigationBar(string path)
        {
            _html.Load(path);
            var actual = MainCore.Parsers.BuildingTabParser.GetNavigationBar(_html);
            actual.ShouldNotBeNull();
        }

        [Theory]
        [InlineData(HeroPage, 3)]
        [InlineData(Marketplace, 5)]
        [InlineData(NewBuilding, 3)]
        [InlineData(Rallypoint, 5)]
        public void CountTab(string path, int expected)
        {
            _html.Load(path);
            var actual = MainCore.Parsers.BuildingTabParser.CountTab(_html);
            actual.ShouldBe(expected);
        }

        [Theory]
        [InlineData(HeroPage, 0)]
        [InlineData(Marketplace, 0)]
        [InlineData(NewBuilding, 0)]
        [InlineData(Rallypoint, 2)]
        public void IsTabActive(string path, int expected)
        {
            _html.Load(path);
            var node = MainCore.Parsers.BuildingTabParser.GetTab(_html, expected);
            var actual = MainCore.Parsers.BuildingTabParser.IsTabActive(node);
            actual.ShouldBeTrue();
        }

        // 2026-10-21: TryGetTab is what SwitchTabCommand's wait predicate uses on a live page. Right
        // after a tab click the browser shows a page with no tab bar (or an empty one); GetTab threw
        // ArgumentOutOfRangeException there and killed the wait (live log 13:01:43).
        [Theory]
        [InlineData(Rallypoint, 2)]
        [InlineData(Rallypoint, 0)]
        [InlineData(HeroPage, 2)]
        public void TryGetTab_ExistingTab_ReturnsSameNodeAsGetTab(string path, int index)
        {
            _html.Load(path);
            var actual = MainCore.Parsers.BuildingTabParser.TryGetTab(_html, index);
            actual.ShouldNotBeNull();
            actual!.XPath.ShouldBe(MainCore.Parsers.BuildingTabParser.GetTab(_html, index).XPath);
        }

        [Theory]
        [InlineData(HeroPage, 3)]
        [InlineData(Rallypoint, 5)]
        [InlineData(Rallypoint, 99)]
        [InlineData(Rallypoint, -1)]
        public void TryGetTab_MissingTab_ReturnsNull(string path, int index)
        {
            _html.Load(path);
            MainCore.Parsers.BuildingTabParser.TryGetTab(_html, index).ShouldBeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("<html><head></head><body></body></html>")]
        [InlineData("<html><body><div class=\"contentNavi subNavi\"></div></body></html>")]
        public void TryGetTab_PageWithoutTabs_ReturnsNull(string html)
        {
            _html.LoadHtml(html);
            MainCore.Parsers.BuildingTabParser.TryGetTab(_html, 2).ShouldBeNull();
        }
    }
}