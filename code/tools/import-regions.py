"""Import pinned AreaCity level-3 CSV + geo CSV; keep centers, discard polygons.

Usage: python tools/import-regions.py regions.csv ok_geo.csv
Source: xiangyuecn/AreaCity-JsSpider-StatsGov, 2025.251231.260403 (MIT).
"""
import csv
import hashlib
import json
import math
import sys
from pathlib import Path

csv.field_size_limit(100_000_000)


def wgs84(lon, lat):
    # Approximate inverse GCJ-02. Weather is queried around the district center.
    x, y = lon - 105, lat - 35
    dlat = -100 + 2*x + 3*y + .2*y*y + .1*x*y + .2*math.sqrt(abs(x))
    dlat += (20*math.sin(6*x*math.pi) + 20*math.sin(2*x*math.pi))*2/3
    dlat += (20*math.sin(y*math.pi) + 40*math.sin(y/3*math.pi))*2/3
    dlat += (160*math.sin(y/12*math.pi) + 320*math.sin(y*math.pi/30))*2/3
    dlon = 300 + x + 2*y + .1*x*x + .1*x*y + .1*math.sqrt(abs(x))
    dlon += (20*math.sin(6*x*math.pi) + 20*math.sin(2*x*math.pi))*2/3
    dlon += (20*math.sin(x*math.pi) + 40*math.sin(x/3*math.pi))*2/3
    dlon += (150*math.sin(x/12*math.pi) + 300*math.sin(x/30*math.pi))*2/3
    rad = lat / 180 * math.pi
    magic = 1 - .00669342162296594323 * math.sin(rad)**2
    dlat = dlat*180 / ((6378245*(1-.00669342162296594323)/(magic*math.sqrt(magic)))*math.pi)
    dlon = dlon*180 / (6378245/math.sqrt(magic)*math.cos(rad)*math.pi)
    return round(lon-dlon, 6), round(lat-dlat, 6)


source, geo_source = (Path(p) for p in sys.argv[1:3])
with geo_source.open(encoding="utf-8-sig", newline="") as f:
    centers = {r["id"]: r["geo"] for r in csv.DictReader(f)}
result = []
with source.open(encoding="utf-8-sig", newline="") as f:
    for row in csv.DictReader(f):
        if row["id"].startswith("91"):  # Source's overseas placeholder is not a province.
            continue
        point = centers[row["id"]]
        lon, lat = (None, None) if point == "EMPTY" else tuple(map(float, point.split()))
        if lon is not None and int(row["id"][:2]) < 71:
            lon, lat = wgs84(lon, lat)
        result.append(dict(Code=row["id"], ParentCode=row["pid"], Level=int(row["deep"]),
                           Name=row["ext_name"], Latitude=lat, Longitude=lon))
target = Path(__file__).resolve().parents[1]/"VPet-Simulator.Windows/assets/regions"
target.mkdir(parents=True, exist_ok=True)
(target/"china-regions.json").write_text(json.dumps(result, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
(target/"source.json").write_text(json.dumps(dict(
    repository="https://github.com/xiangyuecn/AreaCity-JsSpider-StatsGov",
    commit="c6c6e35bea3066d674efe2cded189dc57a86e7d8", release="2025.251231.260403",
    license="MIT", rows=len(result),
    inputSha256={"level3.csv":hashlib.sha256(source.read_bytes()).hexdigest(),
                 "geo.csv":hashlib.sha256(geo_source.read_bytes()).hexdigest()},
    coordinateNote="District centers; mainland GCJ-02 approximately converted to WGS84. Not a street-level forecast."
), ensure_ascii=False, indent=2), encoding="utf-8")
print(f"Imported {len(result)} province/city/district rows.")
