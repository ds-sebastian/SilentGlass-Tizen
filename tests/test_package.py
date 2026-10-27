import json
from pathlib import Path
import subprocess
import sys
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from fetch_artwork import convert, destination


class PackageTests(unittest.TestCase):
    def test_catalog_assets_exist_and_ids_are_unique(self):
        catalog = json.loads((ROOT / "launcher/assets/apps.json").read_text())
        self.assertEqual(len({a["Id"] for a in catalog}), len(catalog))
        for entry in catalog:
            asset = entry["Asset"]
            self.assertNotIn("..", asset)
            self.assertTrue((ROOT / "launcher/assets" / asset).is_file(), asset)

    def test_shell_syntax(self):
        for path in [*(ROOT / "recipes").glob("*.sh"), *(ROOT / "devices/s90d/device").rglob("*.sh")]:
            r = subprocess.run(["sh", "-n"], stdin=path.open(), capture_output=True, text=True)
            self.assertEqual(r.returncode, 0, f"{path.name}: {r.stderr}")

    def test_artwork_destinations_and_active_svg_rejected(self):
        with self.assertRaises(ValueError):
            destination("../elsewhere.svg")
        entry = {"name": "Example", "asset": "app-icons/example.svg",
                 "catalog": {"Width": 100, "Height": 50}}
        valid = b'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 50"><path d="M0 0"/></svg>'
        self.assertEqual(ET.fromstring(convert(valid, entry)).get("width"), "100")
        for data in [
            b'<svg><script>alert(1)</script></svg>',
            b'<svg><image href="https://example.com/image.png"/></svg>',
            b'<svg onload="alert(1)"/>',
            b'<!DOCTYPE svg [<!ENTITY x "x">]><svg/>',
        ]:
            with self.assertRaises(ValueError):
                convert(data, entry)


if __name__ == "__main__":
    unittest.main()
