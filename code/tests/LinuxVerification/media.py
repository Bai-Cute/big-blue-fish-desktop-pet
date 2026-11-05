import os,sys,time,json,subprocess
from pathlib import Path
import xml.etree.ElementTree as ET
payload=Path(sys.argv[1]);data=Path(sys.argv[2]);data.mkdir(parents=True,exist_ok=True)
if any(data.iterdir()):raise RuntimeError('Use a new empty verification data directory')
expected=ET.parse(Path(__file__).resolve().parents[3]/'Version.props').findtext('.//Version')
(data/'preferences.json').write_text(json.dumps(dict(ModelEnabled=False,PublicInfo=False,CheckUpdatesOnStartup=False,AgentEnabled=False,HideOnFullscreen=False)))
env=dict(os.environ,BIGBLUEFISH_DATA=str(data),XDG_SESSION_TYPE='x11')
log=(data/'app.log').open('w');proc=subprocess.Popen([str(payload/'BigBlueFish.Linux'),'--test-mode'],env=env,stdout=log,stderr=log)
def state():
 try:return json.loads((data/'test-status.json').read_text())
 except (FileNotFoundError,json.JSONDecodeError):return {}
def wait(test,timeout=15):
 end=time.monotonic()+timeout
 while time.monotonic()<end:
  if proc.poll() is not None:raise RuntimeError('app stopped: '+(data/'app.log').read_text())
  if test():return state()
  time.sleep(.1)
 raise RuntimeError('timed out: '+str(state())+' '+(data/'app.log').read_text())
def cmd(value):
 (data/'test-command.txt.tmp').write_text(value);(data/'test-command.txt.tmp').replace(data/'test-command.txt')
try:
 s=wait(lambda:state().get('frames',0)>20)
 assert s['version']==expected and s['animationBackend'].startswith(('CPU','GPU'));print('PASS Linux actual GStreamer software/alpha compatibility',flush=True)
 wait(lambda:state().get('phase')=='Idle');print('PASS complete greeting boundary',flush=True)
 cmd('hide');wait(lambda:state().get('hidden'));time.sleep(.5);before=state()['frames'];time.sleep(1.4);assert state()['frames']==before;print('PASS hidden pauses actual playback',flush=True)
 cmd('show');wait(lambda:not state().get('hidden') and state().get('frames')!=before);print('PASS resume advances actual frames',flush=True)
 cmd('settings');wait(lambda:state().get('settingsOpen'));cmd('close-settings');wait(lambda:not state().get('settingsOpen'));print('PASS settings lifecycle',flush=True)
 assert not (data/'test-error.txt').exists();
 if '--safe' in sys.argv:assert not s['McpAvailable'] and not (payload/'BigBlueFish.Control.dll').exists()
 cmd('exit');proc.wait(timeout=6);assert proc.returncode==0;print('PASS clean exit',flush=True)
finally:
 if proc.poll() is None:proc.terminate();proc.wait(timeout=5)
 log.close()
