"""Build compact hit-test masks; video pixels are never read back during GPU playback."""
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import argparse, hashlib, struct, subprocess, zlib
import numpy as np

def build(file):
    target=file.with_suffix('.mask')
    digest=hashlib.sha256(file.read_bytes()).digest()
    if target.exists() and target.read_bytes()[8:16]==struct.pack('<II',320,180) and target.read_bytes()[24:56]==digest:
        return file.name+' cached'
    result=subprocess.run([str(args.ffmpeg),'-v','error','-nostdin','-threads','1','-c:v','libvpx-vp9','-i',str(file),'-an','-sn','-vf','fps=20,alphaextract,scale=320:180:flags=area,format=gray','-f','rawvideo','pipe:1'],capture_output=True,check=True)
    gray=np.frombuffer(result.stdout,dtype=np.uint8)
    if len(gray)==0 or len(gray)%(320*180):raise ValueError(file.name+' invalid frame count')
    packed=np.packbits(gray.reshape(-1,320*180)>=16,axis=1,bitorder='little').tobytes()
    target.write_bytes(struct.pack('<4s5I',b'BBFM',1,320,180,20,len(gray)//(320*180))+digest+zlib.compress(packed,9))
    return file.name+' '+str(target.stat().st_size)+' bytes'

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('directory',type=Path);parser.add_argument('--ffmpeg',type=Path,required=True);args=parser.parse_args()
    with ThreadPoolExecutor(max_workers=3) as pool:
        for result in pool.map(build,sorted(args.directory.glob('*.webm'))):print(result,flush=True)
