using MainCore.Services;

namespace MainCore.Test.Services
{
    public class TravianUrlTest
    {
        [Theory]
        [InlineData("https://ts12.x1.europe.travian.com/resources", true)]
        [InlineData("https://ts12.x1.europe.travian.com/resources?newdid=71146", true)]
        [InlineData("https://ts12.x1.europe.travian.com/resources#", true)]
        [InlineData("https://ts50.x5.international.travian.com/dorf1.php", true)]
        [InlineData("https://ts50.x5.international.travian.com/dorf1.php?newdid=5&", true)]
        [InlineData("https://ts12.x1.europe.travian.com/village", false)]
        [InlineData("https://ts12.x1.europe.travian.com/dorf2.php", false)]
        [InlineData("https://ts12.x1.europe.travian.com/production?t=lumber", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsDorf1(string? url, bool expected) => TravianUrl.IsDorf1(url).ShouldBe(expected);

        [Theory]
        [InlineData("https://ts12.x1.europe.travian.com/village", true)]
        [InlineData("https://ts12.x1.europe.travian.com/village?newdid=71146", true)]
        [InlineData("https://ts50.x5.international.travian.com/dorf2.php?newdid=5", true)]
        [InlineData("https://ts12.x1.europe.travian.com/village/statistics", false)]
        [InlineData("https://ts12.x1.europe.travian.com/resources", false)]
        public void IsDorf2(string? url, bool expected) => TravianUrl.IsDorf2(url).ShouldBe(expected);

        [Theory]
        [InlineData("https://ts12.x1.europe.travian.com/resources", true)]
        [InlineData("https://ts12.x1.europe.travian.com/village", true)]
        [InlineData("https://ts50.x5.international.travian.com/dorf1.php", true)]
        [InlineData("https://ts12.x1.europe.travian.com/building?id=16", false)]
        [InlineData("https://ts12.x1.europe.travian.com/login", false)]
        public void IsDorf(string? url, bool expected) => TravianUrl.IsDorf(url).ShouldBe(expected);

        [Theory]
        [InlineData("https://ts12.x1.europe.travian.com/building?id=16#td", true)]
        [InlineData("https://ts12.x1.europe.travian.com/building?newdid=71146&id=16&&tt=2", true)]
        [InlineData("https://ts12.x1.europe.travian.com/slot?id=3", true)]
        [InlineData("https://ts50.x5.international.travian.com/build.php?id=39&gid=16", true)]
        [InlineData("https://ts12.x1.europe.travian.com/resources", false)]
        [InlineData("https://ts12.x1.europe.travian.com/map", false)]
        public void IsBuilding(string? url, bool expected) => TravianUrl.IsBuilding(url).ShouldBe(expected);

        [Fact]
        public void RelativeUrlsWork()
        {
            TravianUrl.IsDorf1("/resources?x=1").ShouldBeTrue();
            TravianUrl.IsDorf2("dorf2.php").ShouldBeTrue();
        }
    }
}
