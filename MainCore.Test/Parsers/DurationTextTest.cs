using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    public class DurationTextTest
    {
        [Theory]
        [InlineData("0:05:47", 347)]
        [InlineData("2:43:20", 9800)]
        [InlineData("  0:00:30 ", 30)]
        [InlineData("‎0:09:07‎", 547)]
        [InlineData("12:00:00", 43200)]
        public void ParseClockSeconds_ReadsHoursMinutesSeconds(string text, int expected)
        {
            DurationText.ParseClockSeconds(text).ShouldBe(expected);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("5:47")]
        [InlineData("abc")]
        [InlineData("1:2:3:4")]
        public void ParseClockSeconds_Unreadable_IsZero(string text)
        {
            DurationText.ParseClockSeconds(text).ShouldBe(0);
        }

        [Fact]
        public void ParseClockSeconds_Null_IsZero()
        {
            DurationText.ParseClockSeconds(null).ShouldBe(0);
        }
    }
}
