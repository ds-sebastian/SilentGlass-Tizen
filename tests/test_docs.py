"""Documentation: every local link resolves, and the agent guides cite pinned upstream sources."""
import re
import unittest
from pathlib import Path
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
DOCS = sorted({*ROOT.glob("*.md"), *(ROOT / "docs").rglob("*.md"), *(ROOT / "devices").rglob("*.md")})
GUIDES = [ROOT / "docs" / n for n in ("agent-workflow.md", "access-and-persistence.md", "upstream-notes.md")]


class DocumentationTests(unittest.TestCase):
    def test_local_links_resolve(self):
        self.assertGreater(len(DOCS), 10)
        for doc in DOCS:
            for target in re.findall(r"\[[^\]]*\]\(([^ )]+)(?: [^)]*)?\)", doc.read_text()):
                parsed = urlsplit(target)
                if parsed.scheme or parsed.netloc or not parsed.path:
                    continue
                with self.subTest(document=str(doc.relative_to(ROOT)), target=target):
                    self.assertTrue((doc.parent / unquote(parsed.path)).exists())

    def test_guide_citations_have_matching_pinned_sources(self):
        for doc in GUIDES:
            with self.subTest(document=doc.name):
                body, sources = doc.read_text().split("\n## Sources\n", 1)
                cited = set(re.findall(r"\[(\d+)\]", body))
                entries = re.findall(r"^\[(\d+)\] (https://\S+)", sources, re.MULTILINE)
                self.assertTrue(cited, "upstream advice needs source references")
                self.assertEqual(len(entries), len({number for number, _ in entries}))
                self.assertEqual(cited, {number for number, _ in entries})
                for _, url in entries:
                    if "/blob/" in url:
                        self.assertRegex(url.split("/blob/", 1)[1].split("/", 1)[0], r"^[0-9a-f]{40}$")


if __name__ == "__main__":
    unittest.main()
