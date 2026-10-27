"""Undo scripts: the rules the offline sandbox run found mattered on the tested TV (syntax: tests/test_package.py)."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
D = ROOT / "devices/s90d/device"


class UndoTests(unittest.TestCase):
    def test_privacy_restore_follows_its_recorded_sql_and_allows_removed_apps(self):
        # the TV's hash-checked restore.sql demands all 19 apps; Samsung removed one (Amazon Alexa), so restore.sh
        # carries the same SQL with "present => must match its recorded row; absent => skipped"
        recorded = (D / "privacy/restore.sql").read_text()
        script = (D / "privacy/restore.sh").read_text()
        sql = script.split("<<'SQL'\n", 1)[1].split("\nSQL", 1)[0]
        guards = re.findall(r"count\(\*\)=1 FROM package_app_info WHERE app_id='([^']+)' AND (.*);", recorded)
        self.assertEqual(len(guards), 19)
        for app, cond in guards:
            self.assertIn(f"(SELECT count(*) FROM package_app_info WHERE app_id='{app}')="
                          f"(SELECT count(*) FROM package_app_info WHERE app_id='{app}' AND {cond});", sql)
        update = next(l for l in recorded.splitlines() if l.startswith("UPDATE package_app_info"))
        self.assertIn(update, sql)
        self.assertIn("changes()=(SELECT n FROM quiet_want)", sql)
        self.assertNotIn("changes()=19", sql)
        self.assertIn('sha256sum -c files.sha256', script)  # the recorded files are still checked

    def test_prime_restore_accepts_metadata_samsung_put_back(self):
        s = (D / "cleanup/prime-restore.sh").read_text()
        self.assertIn("2) ins=/dev/null", s)
        self.assertIn("refusing", s)  # partial metadata still stops it
        self.assertIn("UPDATE package_app_info SET app_disable='false' WHERE app_id='$COMP'", s)

    def test_stock_home_restore_checks_the_backup_and_deletes_nothing(self):
        s = (D / "launcher-deploy/restore-stock-home.sh").read_text()
        for check in ('sha256sum -c "$D/original-files.sha256"', 'cmp -s "$D/backup-smack-check.txt"',
                      "201:0:755", '[ -d "$A/res/quiet-home" ]', '$PROBE set-home "$o_op"',
                      'buxton2ctl set-string system db/appfw/home_appid "$o_home"'):
            self.assertIn(check, s)
        self.assertLess(s.index("=== HOME ROUTING"), s.index("=== ORIGINAL APP"))  # Home leaves us first
        self.assertNotRegex(s, r"(^|\s)rm\s")
        self.assertIn('mv "$A/bin/.restore-$ts.dll" "$A/bin/BixbyPromotion.dll"', s)  # atomic swap


if __name__ == "__main__":
    unittest.main()
