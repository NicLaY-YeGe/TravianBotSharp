namespace MainCore.Test.Parsers
{
    public class LoginParser : BaseParser
    {
        private const string LoginPage = "Parsers/Login/LoginPage.html";
        private const string LoginPageV2 = "Parsers/Login/LoginPageV2.html";
        private const string Buildings = "Parsers/Login/Buildings.html";
        // 2026-10-18: real TS32 capture of the login page BEFORE its script ran - the form is
        // rendered client-side by React into the empty #loginScene, so there is no password
        // input or button in it at all. Must still count as the login page (and not as
        // in-game), or the bot treats it as "unknown page" and backs off for 30 minutes.
        private const string LoginPageUnrendered = "Parsers/Login/LoginPageUnrendered.html";

        [Theory]
        [InlineData(LoginPage, false)]
        [InlineData(LoginPageV2, false)]
        [InlineData(LoginPageUnrendered, false)]
        [InlineData(Buildings, true)]
        public void IsIngamePage(string file, bool expected)
        {
            _html.Load(file);
            var actual = MainCore.Parsers.LoginParser.IsIngamePage(_html);
            actual.ShouldBe(expected);
        }

        [Theory]
        [InlineData(LoginPage, true)]
        [InlineData(LoginPageV2, true)]
        [InlineData(LoginPageUnrendered, true)]
        [InlineData(Buildings, false)]
        public void IsLoginPage(string file, bool expected)
        {
            _html.Load(file);
            var actual = MainCore.Parsers.LoginParser.IsLoginPage(_html);
            actual.ShouldBe(expected);
        }

        [Theory]
        [InlineData(LoginPage)]
        [InlineData(LoginPageV2)]
        public void GetLoginButton(string file)
        {
            _html.Load(file);
            var actual = MainCore.Parsers.LoginParser.GetLoginButton(_html);
            actual.ShouldNotBeNull();
        }

        [Theory]
        [InlineData(LoginPage)]
        [InlineData(LoginPageV2)]
        public void GetUsernameInput(string file)
        {
            _html.Load(file);
            var actual = MainCore.Parsers.LoginParser.GetUsernameInput(_html);
            actual.ShouldNotBeNull();
        }

        [Theory]
        [InlineData(LoginPage)]
        [InlineData(LoginPageV2)]
        public void GetPasswordInput(string file)
        {
            _html.Load(file);
            var actual = MainCore.Parsers.LoginParser.GetPasswordInput(_html);
            actual.ShouldNotBeNull();
        }
    }
}