"""Offline integrity checks for agent-facing maintenance documentation."""
from pathlib import Path
import re
import unittest
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
GUIDES = tuple(ROOT / "docs" / name for name in (
    "10-agent-workflow.md", "11-access-and-persistence.md", "12-upstream-notes.md",
))


class AgentDocumentationTests(unittest.TestCase):
    def test_local_document_links_resolve(self):
        docs = [ROOT / "README.md", ROOT / "AGENTS.md", ROOT / "CONTRIBUTING.md"]
        docs += sorted((ROOT / "docs").rglob("*.md"))
        docs += sorted((ROOT / "devices").rglob("README.md"))
        for doc in docs:
            for target in re.findall(r"\[[^\]]*\]\(([^ )]+)(?: [^)]*)?\)", doc.read_text()):
                parsed = urlsplit(target)
                if parsed.scheme or parsed.netloc or not parsed.path:
                    continue
                with self.subTest(document=str(doc.relative_to(ROOT)), target=target):
                    self.assertTrue((doc.parent / unquote(parsed.path)).exists())

    def test_guide_citations_have_matching_source_entries(self):
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
                        revision = url.split("/blob/", 1)[1].split("/", 1)[0]
                        self.assertRegex(revision, r"^[0-9a-f]{40}$")


if __name__ == "__main__":
    unittest.main()
