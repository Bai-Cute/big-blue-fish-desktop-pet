"""Fill missing coordinates for 台湾省 cities and districts in china-regions.json.

Usage (after import-regions.py): python tools/fill-taiwan-coordinates.py cities15000.txt
The AreaCity source has no Taiwan coordinates. Names and codes are left unchanged; every city/county
and its districts get the GeoNames point of its seat, so the forecast is at city level, not district level.
"""
import hashlib
import json
import sys
from pathlib import Path

# China city code -> GeoNames id of its seat (cities15000, CC BY 4.0).
SEATS = {
    "7101": "1668341",  # 台北市 Taipei
    "7102": "1673820",  # 高雄市 Kaohsiung
    "7103": "1668355",  # 台南市 Tainan
    "7104": "1668399",  # 台中市 Taichung
    "7106": "1671566",  # 南投县 Nantou
    "7107": "1678228",  # 基隆市 Keelung
    "7108": "1675151",  # 新竹市 Hsinchu
    "7109": "1678836",  # 嘉义市 Chiayi City
    "7111": "1670029",  # 新北市 Banqiao
    "7112": "1674199",  # 宜兰县 Yilan
    "7113": "1677112",  # 新竹县 Zhubei
    "7114": "6696918",  # 桃园市 Taoyuan
    "7115": "1671971",  # 苗栗县 Miaoli
    "7117": "1679136",  # 彰化县 Changhua
    "7119": "1668347",  # 嘉义县 Taibao
    "7121": "1665196",  # 云林县 Douliu
    "7125": "1668295",  # 台东县 Taitung
    "7126": "1674504",  # 花莲县 Hualien City
    "7127": "1672228",  # 澎湖县 Magong
}
# Pingtung City is not in cities15000; its centre is used directly.
FIXED = {"7124": (22.6727, 120.4885)}  # 屏东县 Pingtung

cities = Path(sys.argv[1])
points = {}
with cities.open(encoding="utf-8") as f:
    for line in f:
        row = line.split("\t")
        points[row[0]] = (round(float(row[4]), 6), round(float(row[5]), 6))
seat = {code: points[geonameid] for code, geonameid in SEATS.items()} | FIXED

target = Path(__file__).resolve().parents[1]/"BigBlueFish.Windows/assets/regions"
regions = json.loads((target/"china-regions.json").read_text(encoding="utf-8"))
filled = 0
for region in regions:
    city = region["Code"][:4]
    if region["Code"].startswith("71") and region["Level"] > 0 and region["Latitude"] is None and city in seat:
        region["Latitude"], region["Longitude"] = seat[city]
        filled += 1
missing = [r["Code"] for r in regions if r["Code"].startswith("71") and r["Latitude"] is None]
if missing:
    sys.exit(f"Taiwan rows still without coordinates: {missing}")
(target/"china-regions.json").write_text(json.dumps(regions, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")

source = json.loads((target/"source.json").read_text(encoding="utf-8"))
source["taiwanCoordinates"] = dict(
    note="台湾省 cities and districts use their city/county seat point from GeoNames cities15000 (CC BY 4.0); Pingtung uses a fixed city centre.",
    cities15000Sha256=hashlib.sha256(cities.read_bytes()).hexdigest())
(target/"source.json").write_text(json.dumps(source, ensure_ascii=False, indent=2), encoding="utf-8")
print(f"Filled {filled} Taiwan rows.")
