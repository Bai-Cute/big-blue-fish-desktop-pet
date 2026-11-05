"""Import GeoNames cities15000 into country -> first-level division -> city rows.

Usage: python tools/import-world-regions.py <dir>
<dir> holds GeoNames cities15000.txt (unzipped), admin1CodesASCII.txt, countryInfo.txt
(https://download.geonames.org/export/dump/, CC BY 4.0) and CLDR zh territories.json
(https://cdn.jsdelivr.net/npm/cldr-localenames-full@47.0.0/main/zh/territories.json, Unicode License v3).
China and the US have their own finer datasets; TW/HK/MO are already under China and PR under the US.
Codes: country "JP", division "JP.40", city "JP-1850147" (GeoNames id).
"""
import hashlib
import json
import sys
from collections import defaultdict
from pathlib import Path

SKIP = {"CN", "US", "TW", "HK", "MO", "PR"}
source = Path(sys.argv[1])
files = ["cities15000.txt", "admin1CodesASCII.txt", "countryInfo.txt", "territories.json"]


def rows(name):
    with (source/name).open(encoding="utf-8") as f:
        for line in f:
            if not line.startswith("#") and line.strip():
                yield line.rstrip("\n").split("\t")


english = {f[0]: f[4] for f in rows("countryInfo.txt")}
admin1 = {f[0]: (f[1], f[2]) for f in rows("admin1CodesASCII.txt")}  # code -> (name, ascii)
chinese = json.loads((source/"territories.json").read_text(encoding="utf-8"))["main"]["zh"]["localeDisplayNames"]["territories"]

cities = defaultdict(lambda: defaultdict(list))  # country -> admin1 code -> [city]
for f in rows("cities15000.txt"):
    geonameid, name, ascii_name, lat, lon, country, a1 = f[0], f[1], f[2], f[4], f[5], f[8], f[10]
    if country in SKIP or country not in english:
        continue
    key = f"{country}.{a1}" if f"{country}.{a1}" in admin1 else f"{country}.00"
    cities[country][key].append((ascii_name.casefold(), name, geonameid, round(float(lat), 4), round(float(lon), 4)))

countries, regions = [], []
for country in sorted(cities, key=lambda c: english[c].casefold()):
    countries.append(dict(Code=country, Name=chinese.get(country, english[country]), EnglishName=english[country]))
    divisions = cities[country]

    def division_name(key):
        return admin1[key] if key in admin1 else ("其他 Other", "Other")
    for key in sorted(divisions, key=lambda k: division_name(k)[1].casefold()):
        regions.append(dict(Code=key, ParentCode=country, Level=0, Name=division_name(key)[0], Latitude=None, Longitude=None))
        for _, name, geonameid, lat, lon in sorted(divisions[key]):
            regions.append(dict(Code=f"{country}-{geonameid}", ParentCode=key, Level=1, Name=name, Latitude=lat, Longitude=lon))

target = Path(__file__).resolve().parents[1]/"BigBlueFish.Windows/assets/regions"
target.mkdir(parents=True, exist_ok=True)
(target/"world-countries.json").write_text(json.dumps(countries, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
(target/"world-regions.json").write_text(json.dumps(regions, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
(target/"world-source.json").write_text(json.dumps(dict(
    geonames="https://download.geonames.org/export/dump/ (cities15000, admin1CodesASCII, countryInfo)",
    geonamesLicense="CC BY 4.0, GeoNames (https://www.geonames.org/)",
    countryNames="Unicode CLDR 47.0.0 zh territories (Unicode License v3)",
    countries=len(countries), rows=len(regions),
    inputSha256={name: hashlib.sha256((source/name).read_bytes()).hexdigest() for name in files},
    coordinateNote="City points from GeoNames (WGS84), cities with population over 15000 plus capitals. Not a street-level forecast."
), ensure_ascii=False, indent=2), encoding="utf-8")
print(f"Imported {len(countries)} countries and {len(regions)} division/city rows.")
