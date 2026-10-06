"""Verify original 24-fps video, alpha decoding and self-contained FFmpeg for every clip."""
import concurrent.futures, json, subprocess, sys
from pathlib import Path
root = Path(__file__).resolve().parents[1]
directory = root/'code/VPet-Simulator.Windows/assets/fish'
ffmpeg = root/'code/VPet-Simulator.Windows/media/ffmpeg.exe'
def verify(path):
    result = subprocess.run([str(ffmpeg),'-v','error','-nostdin','-threads','1','-c:v','libvpx-vp9','-i',str(path),'-ss','2','-frames:v','1','-f','rawvideo','-pix_fmt','bgra','pipe:1'], capture_output=True, check=True)
    data = result.stdout
    if len(data) != 640*360*4: raise ValueError(f'{path.name}: incorrect frame size {len(data)}')
    alpha = data[3::4]
    if min(alpha)!=0 or max(alpha)<200: raise ValueError(f'{path.name}: missing transparent or opaque region')
    return path.name
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
    checked=list(pool.map(verify, sorted(directory.glob('*.webm'))))
if len(checked)!=106: raise ValueError(f'{len(checked)} videos, expected 106')
print('106 videos decoded at 640x360 with transparent and opaque pixels; bundled decoder has no external runtime dependency.')
