namespace MainCore.Test.Parsers
{
    public class TrainQueueParserTest
    {
        private static HtmlDocument Doc(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc;
        }

        private const string ValueAttribute =
            "<table class=\"under_progress\"><tbody><tr><td>5 Phalanx</td><td class=\"dur\"><span class=\"timer\" value=\"754\">0:12:34</span></td></tr></tbody></table>";

        private const string DataValueAttribute =
            "<table class=\"under_progress\"><tbody><tr><td>x</td><td><span class=\"timer\" data-value=\"90\">0:01:30</span></td></tr></tbody></table>";

        [Fact]
        public void GetSecondsRemaining_ReadsValueAttribute()
        {
            TrainQueueParser.GetSecondsRemaining(Doc(ValueAttribute)).ShouldBe(754);
        }

        [Fact]
        public void GetSecondsRemaining_ReadsDataValueAttribute()
        {
            TrainQueueParser.GetSecondsRemaining(Doc(DataValueAttribute)).ShouldBe(90);
        }

        [Fact]
        public void GetSecondsRemaining_FirstTimerWins()
        {
            var html = "<table class=\"under_progress\"><tr><td><span class=\"timer\" value=\"100\"></span></td></tr><tr><td><span class=\"timer\" value=\"900\"></span></td></tr></table>";
            TrainQueueParser.GetSecondsRemaining(Doc(html)).ShouldBe(100);
            TrainQueueParser.CountTimers(Doc(html)).ShouldBe(2);
        }

        [Fact]
        public void GetSecondsRemaining_NoTableOrNoTimerIsNull()
        {
            TrainQueueParser.GetSecondsRemaining(Doc("<div>idle page</div>")).ShouldBeNull();
            TrainQueueParser.GetSecondsRemaining(Doc("<table class=\"under_progress\"><tr><td>nothing</td></tr></table>")).ShouldBeNull();
            TrainQueueParser.HasQueueTable(Doc("<table class=\"under_progress\"></table>")).ShouldBeTrue();
            TrainQueueParser.HasQueueTable(Doc("<div></div>")).ShouldBeFalse();
        }

        [Fact]
        public void GetSecondsRemaining_RejectsZeroNegativeAndImplausibleValues()
        {
            foreach (var bad in new[] { "0", "-5", "abc", "99999999" })
            {
                var html = $"<table class=\"under_progress\"><tr><td><span class=\"timer\" value=\"{bad}\"></span></td></tr></table>";
                TrainQueueParser.GetSecondsRemaining(Doc(html)).ShouldBeNull();
            }
        }

        [Fact]
        public void SmithyAndTownHallParsers_KeepTheirOwnRead_AndAcceptValueMarkupToo()
        {
            SmithyParser.GetOngoingResearchSecondsRemaining(Doc(DataValueAttribute)).ShouldBe(90);
            SmithyParser.GetOngoingResearchSecondsRemaining(Doc(ValueAttribute)).ShouldBe(754);
            TownHallParser.GetOngoingCelebrationSecondsRemaining(Doc(DataValueAttribute)).ShouldBe(90);
            TownHallParser.GetOngoingCelebrationSecondsRemaining(Doc(ValueAttribute)).ShouldBe(754);
            SmithyParser.GetOngoingResearchSecondsRemaining(Doc("<div></div>")).ShouldBeNull();
            TownHallParser.GetOngoingCelebrationSecondsRemaining(Doc("<div></div>")).ShouldBeNull();
        }
    }
}
