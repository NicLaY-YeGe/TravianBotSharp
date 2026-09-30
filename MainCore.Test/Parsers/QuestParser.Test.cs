namespace MainCore.Test.Parsers
{
    public class QuestParser : BaseParser
    {
        private const string QuestClaimable = "Parsers/Quest/QuestClaimable.html";
        private const string QuestNotClaimable = "Parsers/Quest/QuestNotClaimable.html";
        private const string QuestPage = "Parsers/Quest/QuestPage.html";

        [Theory]
        [InlineData(QuestClaimable)]
        [InlineData(QuestNotClaimable)]
        public void GetQuestMaster(string file)
        {
            _html.Load(file);
            var actual = MainCore.Parsers.QuestParser.GetQuestMaster(_html);
            actual.ShouldNotBeNull();
        }

        [Theory]
        [InlineData(QuestClaimable, true)]
        [InlineData(QuestNotClaimable, false)]
        public void IsQuestClaimable(string file, bool expected)
        {
            _html.Load(file);
            var actual = MainCore.Parsers.QuestParser.IsQuestClaimable(_html);
            actual.ShouldBe(expected);
        }

        [Fact]
        public void GetQuestCollectButton()
        {
            _html.Load(QuestPage);
            var result = MainCore.Parsers.QuestParser.GetQuestCollectButton(_html);
            result.ShouldNotBeNull();
        }

        [Fact]
        public void IsQuestPage()
        {
            _html.Load(QuestPage);
            var result = MainCore.Parsers.QuestParser.IsQuestPage(_html);
            result.ShouldBeTrue();
        }

        [Fact]
        public void GetNextPageButton_PageOneOfThree_ReturnsForwardButton()
        {
            // QuestPage.html is a real capture sitting on page 1 of a 3-page task list (see
            // QuestParser.GetNextPageButton's comment / the 2026-09-29 pagination bug).
            _html.Load(QuestPage);
            var result = MainCore.Parsers.QuestParser.GetNextPageButton(_html);
            result.ShouldNotBeNull();
        }

        [Theory]
        [InlineData(QuestClaimable)]
        [InlineData(QuestNotClaimable)]
        public void GetNextPageButton_NoPagination_ReturnsNull(string file)
        {
            _html.Load(file);
            var result = MainCore.Parsers.QuestParser.GetNextPageButton(_html);
            result.ShouldBeNull();
        }
    }
}