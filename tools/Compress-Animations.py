"""Encode the approved transparent VP9 variant, verifying every clip before replacement.

Usage: python tools/Compress-Animations.py OUTPUT_DIRECTORY [--apply]
The output directory must be outside assets/fish. Source fingerprints are checked
again before --apply, so a changed source is never replaced by a stale encode.
"""
import concurrent.futures
import hashlib
import json
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'code/BigBlueFish.Windows/assets/fish'
OUT = pathlib.Path(sys.argv[1]).resolve()
if OUT == SOURCE or SOURCE in OUT.parents:
    raise SystemExit('Use a separate work directory.')
OUT.mkdir(parents=True, exist_ok=True)

def sha(p):
    with p.open('rb') as f:
        return hashlib.file_digest(f, 'sha256').hexdigest()

def probe(p):
    j = json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-count_frames',
        '-show_streams', '-show_format', '-of', 'json', str(p)]))
    v = next(s for s in j['streams'] if s['codec_type'] == 'video')
    assert (v['width'], v['height'], v['avg_frame_rate']) == (1280, 720, '60/1'), p
    assert {k.lower(): x for k, x in v.get('tags', {}).items()}.get('alpha_mode') == '1', p
    return {'frames': int(v['nb_read_frames']), 'duration': float(j['format']['duration']), 'bytes': p.stat().st_size}

def frame(p, t):
    return subprocess.check_output(['ffmpeg', '-v', 'error', '-nostdin', '-threads', '1',
        '-c:v', 'libvpx-vp9', '-ss', str(t), '-i', str(p), '-frames:v', '1', '-f', 'rawvideo', '-pix_fmt', 'rgba', 'pipe:1'])

def encode(p):
    target = OUT / p.name
    fingerprint = sha(p)
    record = OUT / (p.stem + '.json')
    if target.exists() and record.exists():
        old = json.loads(record.read_text(encoding='utf-8'))
        if old['source_sha256'] == fingerprint and old['output_sha256'] == sha(target):
            return old
        raise RuntimeError('Stale output: ' + str(target))
    original = probe(p)
    temporary = target.with_suffix('.partial.webm')
    subprocess.run(['ffmpeg', '-v', 'error', '-nostdin', '-threads', '2', '-c:v', 'libvpx-vp9', '-i', str(p),
        '-map', '0:v:0', '-an', '-c:v', 'libvpx-vp9', '-b:v', '0', '-crf', '34', '-deadline', 'good', '-cpu-used', '2',
        '-lag-in-frames', '25', '-auto-alt-ref', '0', '-row-mt', '1', '-threads', '2', '-pix_fmt', 'yuva420p',
        '-fps_mode', 'passthrough', str(temporary)], check=True, capture_output=True)
    compressed = probe(temporary)
    assert original['frames'] == compressed['frames'] and abs(original['duration'] - compressed['duration']) < .002, p
    samples = []
    for fraction in (.1, .5, .8):
        t = max(0, (original['duration'] - .1) * fraction)
        a, b = frame(p, t)[3::4], frame(temporary, t)[3::4]
        assert len(a) == len(b) == 1280 * 720 and min(b) == 0 and max(b) > 240, p
        union = sum(x > 128 or y > 128 for x, y in zip(a, b))
        iou = sum(x > 128 and y > 128 for x, y in zip(a, b)) / max(1, union)
        assert iou > .97, (p, iou)
        samples.append(iou)
    assert sha(p) == fingerprint, 'Source changed during encoding'
    temporary.replace(target)
    result = dict(file=p.name, source_sha256=fingerprint, output_sha256=sha(target), original=original, compressed=compressed, alpha_iou=samples)
    record.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f"PASS {p.name}: {original['bytes']} -> {compressed['bytes']}", flush=True)
    return result

if '--apply' in sys.argv:
    records = json.loads((OUT / 'manifest.json').read_text(encoding='utf-8'))
    assert len(records) == 106
    for r in records:
        assert sha(SOURCE / r['file']) == r['source_sha256'] and sha(OUT / r['file']) == r['output_sha256'], r['file']
    for r in records:
        (OUT / r['file']).replace(SOURCE / r['file'])
    print('Applied 106 verified animations; compat/ assets unchanged.')
else:
    files = sorted(SOURCE.glob('*.webm'))
    assert len(files) == 106
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        records = list(pool.map(encode, files))
    (OUT / 'manifest.json').write_text(json.dumps(records, ensure_ascii=False, indent=2), encoding='utf-8')
    print('COMPLETE', sum(x['original']['bytes'] for x in records), sum(x['compressed']['bytes'] for x in records), flush=True)
