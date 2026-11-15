using MainCore.Services;

namespace MainCore.Test.Services
{
    public class BrowserErrorRulesTest
    {
        [Theory]
        [InlineData("Unable to retrieve content because the page is navigating and changing the content.")]
        [InlineData("Script failed: Execution context was destroyed, most likely because of a navigation")]
        [InlineData("Frame was detached")]
        [InlineData("UNABLE TO RETRIEVE CONTENT BECAUSE THE PAGE IS NAVIGATING")]
        public void IsTransient_TrueForNavigationInFlight(string message)
        {
            BrowserErrorRules.IsTransient(message).ShouldBeTrue();
        }

        [Theory]
        [InlineData("Target page, context or browser has been closed")]
        [InlineData("Target closed")]
        [InlineData("Browser closed")]
        [InlineData("The browser page is not available (closed?).")]
        [InlineData("Some unknown failure")]
        [InlineData("")]
        public void IsTransient_FalseForGoneOrUnknown(string message)
        {
            BrowserErrorRules.IsTransient(message).ShouldBeFalse();
        }

        [Fact]
        public void IsTransient_FalseForNull()
        {
            BrowserErrorRules.IsTransient(null).ShouldBeFalse();
        }

        [Fact]
        public void IsTransient_GoneWinsWhenBothAppear()
        {
            BrowserErrorRules.IsTransient("Target closed while the page is navigating").ShouldBeFalse();
        }
    }
}
