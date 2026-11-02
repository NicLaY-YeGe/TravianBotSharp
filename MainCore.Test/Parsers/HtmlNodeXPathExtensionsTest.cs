using HtmlAgilityPack;
using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    public class HtmlNodeXPathExtensionsTest
    {
        private static HtmlDocument Load(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            return doc;
        }

        [Fact]
        public void StableXPath_AnchorsAtNearestAncestorWithUniqueId()
        {
            var doc = Load("<html><body><div id=\"a\"></div><div id=\"villageContent\"><div></div><div class=\"x\"></div></div></body></html>");
            var node = doc.DocumentNode.SelectSingleNode("//*[@id='villageContent']/div[2]");

            node.StableXPath().ShouldBe("//*[@id='villageContent']/div[2]");
        }

        [Fact]
        public void StableXPath_DoesNotDependOnSiblingsAboveTheAnchor()
        {
            // Same target, but two extra divs in front of the anchor (banner / injected element):
            // the absolute XPath changes, the stable one does not - that is the whole point.
            var before = Load("<html><body><div id=\"villageContent\"><div></div><div class=\"t\"></div></div></body></html>");
            var after = Load("<html><body><div class=\"banner\"></div><div class=\"ext\"></div><div id=\"villageContent\"><div></div><div class=\"t\"></div></div></body></html>");

            var nodeBefore = before.DocumentNode.SelectSingleNode("//div[@class='t']");
            var nodeAfter = after.DocumentNode.SelectSingleNode("//div[@class='t']");

            (nodeBefore.XPath == nodeAfter.XPath).ShouldBeFalse();
            nodeBefore.StableXPath().ShouldBe(nodeAfter.StableXPath());
        }

        [Fact]
        public void StableXPath_NodeWithOwnId_UsesIt()
        {
            var doc = Load("<html><body><div><a id=\"go\" href=\"#\">x</a></div></body></html>");
            var node = doc.DocumentNode.SelectSingleNode("//a");

            node.StableXPath().ShouldBe("//*[@id='go']");
        }

        [Fact]
        public void StableXPath_NoIdAnywhere_FallsBackToAbsoluteXPath()
        {
            var doc = Load("<html><body><div><span></span><span class=\"t\"></span></div></body></html>");
            var node = doc.DocumentNode.SelectSingleNode("//span[@class='t']");

            node.StableXPath().ShouldBe(node.XPath);
        }

        [Fact]
        public void StableXPath_DuplicateId_IsSkippedForTheNextAnchor()
        {
            var doc = Load("<html><body><div id=\"outer\"><div id=\"dup\"><p class=\"t\"></p></div><div id=\"dup\"></div></div></body></html>");
            var node = doc.DocumentNode.SelectSingleNode("//p[@class='t']");

            node.StableXPath().ShouldBe("//*[@id='outer']/div[1]/p[1]");
        }

        [Fact]
        public void StableXPath_IdWithQuote_FallsBackToAbsoluteXPath()
        {
            var doc = Load("<html><body><div id=\"it's\"><p class=\"t\"></p></div></body></html>");
            var node = doc.DocumentNode.SelectSingleNode("//p[@class='t']");

            node.StableXPath().ShouldBe(node.XPath);
        }

        [Fact]
        public void StableXPath_ResultSelectsTheSameNodeAsTheAbsoluteXPath()
        {
            var doc = Load("<html><body><div class=\"a\"></div><div id=\"villageContent\">" + string.Concat(Enumerable.Range(0, 25).Select(i => $"<div class=\"slot{i}\"></div>")) + "</div></body></html>");
            var node = doc.DocumentNode.SelectSingleNode("//div[@class='slot20']");

            var stable = node.StableXPath();

            stable.ShouldBe("//*[@id='villageContent']/div[21]");
            doc.DocumentNode.SelectSingleNode(stable).ShouldBe(node);
        }

        [Fact]
        public void StableXPath_IndexBoundary_DoesNotMixUpDiv1AndDiv10()
        {
            // "div[1]" is a string prefix of "div[10]": the anchor check must respect the "/" boundary.
            var doc = Load("<html><body><div><div id=\"x\"></div></div></body></html>");
            var node = doc.DocumentNode.SelectSingleNode("//*[@id='x']");

            node.StableXPath().ShouldBe("//*[@id='x']");
        }

        // Real dorf2 capture: the Rally Point slot (location 39) is the 21st div of #villageContent -
        // exactly the element the live log could not find through its absolute XPath.
        [Fact]
        public void StableXPath_RealVillagePage_RallyPointSlotIsAnchoredAtVillageContent()
        {
            var doc = new HtmlDocument();
            doc.Load("Parsers/BuildingLayout/Buildings.html");
            var node = doc.DocumentNode.SelectSingleNode("//*[@id='villageContent']/div[21]");
            node.ShouldNotBeNull();

            node.StableXPath().ShouldBe("//*[@id='villageContent']/div[21]");
            doc.DocumentNode.SelectSingleNode(node.StableXPath()).ShouldBe(node);
        }
    }
}
