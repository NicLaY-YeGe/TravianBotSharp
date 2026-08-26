namespace MainCore.Parsers
{
    public static class LoginParser
    {
        public static HtmlNode? GetLoginButton(HtmlDocument doc)
        {
            // 2026-08-26: Travian redesigned the login/lobby screen (every class name in the
            // new markup is suffixed "V2" - formV2, textButtonV2, etc.) and the old fixed
            // container id="loginScene" is gone (the new wrapper is id="dialogContent", which
            // is too generic/reused elsewhere to anchor on directly). Anchoring on the
            // password input instead - it's a reliable, version-agnostic signal that we're on
            // a login form - then walking up its ancestors looking for the "green" submit
            // button keeps this scoped to the login form itself (an in-game page never has a
            // name="password" input, so this never even starts searching there) instead of
            // matching the first "green" button anywhere in the whole document.
            var passwordInput = GetPasswordInput(doc);
            if (passwordInput is null) return null;

            for (var ancestor = passwordInput.ParentNode; ancestor is not null; ancestor = ancestor.ParentNode)
            {
                var loginButton = ancestor
                    .Descendants("button")
                    .FirstOrDefault(x => x.HasClass("green"));
                if (loginButton is not null) return loginButton;
            }

            return null;
        }

        public static HtmlNode? GetUsernameInput(HtmlDocument doc)
        {
            var usernameInput = doc.DocumentNode
                .Descendants("input")
                .FirstOrDefault(x => x.GetAttributeValue("name", "").Equals("name"));
            return usernameInput;
        }

        public static HtmlNode? GetPasswordInput(HtmlDocument doc)
        {
            var passwordInput = doc.DocumentNode
                .Descendants("input")
                .FirstOrDefault(x => x.GetAttributeValue("name", "").Equals("password"));
            return passwordInput;
        }

        public static bool IsIngamePage(HtmlDocument doc)
        {
            var serverTime = doc.GetElementbyId("servertime");
            return serverTime is not null;
        }

        public static bool IsLoginPage(HtmlDocument doc)
        {
            var loginButton = GetLoginButton(doc);
            return loginButton is not null;
        }
    }
}