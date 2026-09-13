import sqlite3
import tempfile
import unittest
from pathlib import Path
from urllib.error import URLError

import sync_player_profiles as sync


def fixture(code="62404", **changes):
    fields = dict(lblName="구자욱", lblBackNo="5", lblBirthday="1993년 02월 12일",
                  lblPosition="외야수(우투좌타)", lblHeightWeight="189cm/75kg",
                  lblCareer="본리초-경복중-대구고-삼성-상무", lblPayment="13000만원",
                  lblSalary="50000만원", lblDraft="12 삼성 2라운드 12순위", lblJoinInfo="12삼성")
    fields.update(changes)
    spans = "".join(f'<span id="x_playerProfile_{key}">{value}</span>' for key, value in fields.items() if value is not None)
    return f'<h4 id="h4Team"><span><img src="team.png"></span>삼성 라이온즈</h4><div><img id="x_playerProfile_imgProgile" src="//6ptotvmi5753.edge.naverncp.com/KBO_IMAGE/person/middle/2026/{code}.jpg">{spans}</div><h6>2026 성적</h6>'


class ProfileTests(unittest.TestCase):
    def test_official_fields_and_salary_not_assigned_statistics_year(self):
        p = sync.parse_profile(fixture(), "62404", "https://www.koreabaseball.com/")
        self.assertEqual((p["Name"], p["TeamName"], p["BirthDate"], p["Position"], p["BatsThrows"]),
                         ("구자욱", "삼성 라이온즈", "1993-02-12", "외야수", "우투좌타"))
        self.assertEqual((p["HeightCm"], p["WeightKg"], p["SalaryText"]), (189, 75, "50000만원"))
        self.assertIsNone(p["ProfileSeason"])

    def test_present_blank_fields_are_unknown(self):
        p = sync.parse_profile(fixture(lblSalary="-", lblPayment="", lblHeightWeight=""), "62404", "")
        self.assertIsNone(p["SalaryText"])
        self.assertIsNone(p["SigningBonusText"])
        self.assertIsNone(p["HeightCm"])

    def test_partial_markup_and_wrong_player_rejected(self):
        for html in [fixture(lblSalary=None), fixture(code="12345"), "<html>선수 없음</html>"]:
            with self.subTest(html=html[:60]), self.assertRaises(ValueError):
                sync.parse_profile(html, "62404", "")

    def test_only_observed_official_portrait_urls(self):
        good = "https://6ptotvmi5753.edge.naverncp.com/KBO_IMAGE/person/middle/2026/62404.jpg"
        self.assertEqual(sync.photo_url(good, "62404"), good)
        for url in [good.replace("https:", "http:"), good.replace("62404", "12345"),
                    "https://attacker.invalid/62404.jpg", "//www.koreabaseball.com/no-Image.png",
                    good + "?url=bad", "file:///tmp/62404.jpg"]:
            self.assertIsNone(sync.photo_url(url, "62404"))

    def test_non_image_response_rejected(self):
        with self.assertRaises(ValueError):
            sync.image_extension(b"<html>Image not found</html>" * 10)

    def test_failed_fetch_keeps_existing_profile(self):
        class Offline:
            def get(self, *args, **kwargs):
                raise URLError("offline")
        with tempfile.TemporaryDirectory() as directory:
            c = sqlite3.connect(":memory:")
            c.row_factory = sqlite3.Row
            c.execute(sync.SCHEMA)
            sync.store_profile(c, dict(Pcode="62404", Name="구자욱", SalaryText="50000만원"))
            old = c.execute("SELECT * FROM OfficialPlayerProfiles").fetchone()
            with self.assertRaises(URLError):
                sync.sync_one(c, Offline(), "62404", Path(directory), old)
            self.assertEqual(c.execute("SELECT SalaryText FROM OfficialPlayerProfiles").fetchone()[0], "50000만원")

    def test_photo_failure_preserves_previous_photo_with_new_valid_profile(self):
        class PhotoOffline:
            def get(self, url, limit, is_photo=False):
                if is_photo:
                    raise URLError("photo offline")
                return fixture().encode()
        with tempfile.TemporaryDirectory() as directory:
            photos = Path(directory)
            (photos / "62404.jpg").write_bytes(b"old photo bytes")
            c = sqlite3.connect(":memory:")
            c.row_factory = sqlite3.Row
            c.execute(sync.SCHEMA)
            sync.store_profile(c, dict(Pcode="62404", Name="구자욱", PhotoFileName="62404.jpg", PhotoSourceUrl="old source"))
            old = c.execute("SELECT * FROM OfficialPlayerProfiles").fetchone()
            data, warning = sync.sync_one(c, PhotoOffline(), "62404", photos, old)
            self.assertIsNotNone(warning)
            self.assertEqual(data["PhotoFileName"], "62404.jpg")
            self.assertEqual(data["PhotoSourceUrl"], "old source")
            self.assertEqual((photos / "62404.jpg").read_bytes(), b"old photo bytes")


if __name__ == "__main__":
    unittest.main()
