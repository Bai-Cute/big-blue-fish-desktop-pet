"""Import the US Census Gazetteer county file into state -> county rows.

Usage: python tools/import-us-regions.py 2026_Gaz_counties_national.txt
Source: https://www2.census.gov/geo/docs/maps-data/data/gazetteer/2026_Gazetteer/2026_Gaz_counties_national.zip
(US Census Bureau, public domain). Codes are "US-" + FIPS: state US-06, county US-06037.
"""
import csv
import hashlib
import json
import sys
from pathlib import Path

# USPS -> (state FIPS, Chinese name)
STATES = {
    "AL": ("01", "亚拉巴马州"), "AK": ("02", "阿拉斯加州"), "AZ": ("04", "亚利桑那州"), "AR": ("05", "阿肯色州"),
    "CA": ("06", "加利福尼亚州"), "CO": ("08", "科罗拉多州"), "CT": ("09", "康涅狄格州"), "DE": ("10", "特拉华州"),
    "DC": ("11", "哥伦比亚特区"), "FL": ("12", "佛罗里达州"), "GA": ("13", "佐治亚州"), "HI": ("15", "夏威夷州"),
    "ID": ("16", "爱达荷州"), "IL": ("17", "伊利诺伊州"), "IN": ("18", "印第安纳州"), "IA": ("19", "艾奥瓦州"),
    "KS": ("20", "堪萨斯州"), "KY": ("21", "肯塔基州"), "LA": ("22", "路易斯安那州"), "ME": ("23", "缅因州"),
    "MD": ("24", "马里兰州"), "MA": ("25", "马萨诸塞州"), "MI": ("26", "密歇根州"), "MN": ("27", "明尼苏达州"),
    "MS": ("28", "密西西比州"), "MO": ("29", "密苏里州"), "MT": ("30", "蒙大拿州"), "NE": ("31", "内布拉斯加州"),
    "NV": ("32", "内华达州"), "NH": ("33", "新罕布什尔州"), "NJ": ("34", "新泽西州"), "NM": ("35", "新墨西哥州"),
    "NY": ("36", "纽约州"), "NC": ("37", "北卡罗来纳州"), "ND": ("38", "北达科他州"), "OH": ("39", "俄亥俄州"),
    "OK": ("40", "俄克拉何马州"), "OR": ("41", "俄勒冈州"), "PA": ("42", "宾夕法尼亚州"), "RI": ("44", "罗得岛州"),
    "SC": ("45", "南卡罗来纳州"), "SD": ("46", "南达科他州"), "TN": ("47", "田纳西州"), "TX": ("48", "得克萨斯州"),
    "UT": ("49", "犹他州"), "VT": ("50", "佛蒙特州"), "VA": ("51", "弗吉尼亚州"), "WA": ("53", "华盛顿州"),
    "WV": ("54", "西弗吉尼亚州"), "WI": ("55", "威斯康星州"), "WY": ("56", "怀俄明州"), "PR": ("72", "波多黎各"),
}

source = Path(sys.argv[1])
counties = {}
with source.open(encoding="utf-8-sig", newline="") as f:
    for row in csv.DictReader(f, delimiter="|"):
        row = {k.strip(): v.strip() for k, v in row.items()}
        counties.setdefault(row["USPS"], []).append(row)
missing = set(counties) - set(STATES)
if missing:
    sys.exit(f"Unknown state codes: {sorted(missing)}")

result = []
for usps, (fips, name) in sorted(STATES.items(), key=lambda s: s[1][0]):
    rows = counties.get(usps, [])
    if not rows:
        sys.exit(f"No counties for {usps}")
    lat = round(sum(float(r["INTPTLAT"]) for r in rows) / len(rows), 6)
    lon = round(sum(float(r["INTPTLONG"]) for r in rows) / len(rows), 6)
    result.append(dict(Code="US-" + fips, ParentCode="US", Level=0, Name=f"{name} {usps}", Latitude=lat, Longitude=lon))
    for r in sorted(rows, key=lambda r: r["NAME"]):
        result.append(dict(Code="US-" + r["GEOID"], ParentCode="US-" + fips, Level=1, Name=r["NAME"],
                           Latitude=round(float(r["INTPTLAT"]), 6), Longitude=round(float(r["INTPTLONG"]), 6)))

target = Path(__file__).resolve().parents[1]/"BigBlueFish.Windows/assets/regions"
target.mkdir(parents=True, exist_ok=True)
(target/"us-regions.json").write_text(json.dumps(result, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
(target/"us-source.json").write_text(json.dumps(dict(
    source="https://www2.census.gov/geo/docs/maps-data/data/gazetteer/2026_Gazetteer/2026_Gaz_counties_national.zip",
    publisher="US Census Bureau", vintage="2026", license="Public domain (US federal government work)", rows=len(result),
    inputSha256={source.name: hashlib.sha256(source.read_bytes()).hexdigest()},
    coordinateNote="County internal points (INTPTLAT/INTPTLONG, WGS84). Not a street-level forecast."
), ensure_ascii=False, indent=2), encoding="utf-8")
print(f"Imported {len(result)} state/county rows.")
