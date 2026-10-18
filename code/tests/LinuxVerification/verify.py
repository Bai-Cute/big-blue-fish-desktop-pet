#!/usr/bin/env python3
"""Run against an actual published Linux app under an isolated X11 desktop."""
import base64, json, os, pathlib, queue, subprocess, sys, threading, time

payload = pathlib.Path(sys.argv[1]).resolve()
data = pathlib.Path(sys.argv[2]).resolve()
data.mkdir(parents=True, exist_ok=True)
env = dict(os.environ, BIGBLUEFISH_DATA=str(data), XDG_SESSION_TYPE="x11")
count = 0
def check(ok, label):
    global count
    assert ok, label
    count += 1
    print(f"PASS {count}: {label}", flush=True)
def wait(condition, seconds=12):
    until = time.monotonic() + seconds
    while time.monotonic() < until:
        try:
            result = condition()
            if result: return result
        except (FileNotFoundError, json.JSONDecodeError): pass
        time.sleep(.08)
    raise TimeoutError(str(condition))
def state(): return json.loads((data / 'test-status.json').read_text())
def command(value):
    temporary=data/'test-command.txt.tmp';temporary.write_text(value);temporary.replace(data/'test-command.txt')
for name in ['test-status.json','test-error.txt','test-command.txt']:
    (data / name).unlink(missing_ok=True)
(data / 'preferences.json').write_text(json.dumps(dict(ModelEnabled=False,PublicInfo=False,CheckUpdatesOnStartup=False,McpEnabled=True,PuppetMode=True)))
log = (data / 'app.log').open('w')
app = subprocess.Popen([str(payload / 'BigBlueFish.Linux'), '--test-mode'], env=env, stdout=log, stderr=log)
bridge = fixture = None
try:
    wait(lambda: state().get('frames',0) > 10)
    check(app.poll() is None, 'actual self-contained Linux window running')
    if '--safe' in sys.argv:
        check(not state()['McpAvailable'] and not state()['Puppet'], 'safe ignores enabled MCP and puppet preferences')
        check(not (payload/'BigBlueFish.Mcp').exists() and not (payload/'BigBlueFish.Control.dll').exists(), 'safe package contains no MCP bridge or Control assembly')
        check('BigBlueFish.Control' not in (payload/'BigBlueFish.Linux.deps.json').read_text(), 'safe executable has no MCP dependency')
        command('settings'); wait(lambda: state()['settingsOpen'])
        command('render-settings'); wait(lambda: (data/'settings.png').exists())
        command('close-settings'); wait(lambda: not state()['settingsOpen'])
        check(state()['phase'] != 'PuppetIdle', 'safe continues ordinary animation selection')
        check(not (data/'test-error.txt').exists(),'no safe application errors')
        print(f'Linux Safe actual integration: {count} checks passed.', flush=True)
        sys.exit(0)
    check(state()['Puppet'] and state()['currentAction']=='待机呼吸休闲', 'startup puppet uses fixed breathing idle')
    bridge = subprocess.Popen([str(payload / 'BigBlueFish.Mcp')], env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, bufsize=1)
    lines = queue.Queue(); events=[]; request_id=0
    def receive():
        for line in bridge.stdout: lines.put(json.loads(line))
    threading.Thread(target=receive, daemon=True).start()
    def request(method, params={}):
        global request_id
        request_id+=1; number=request_id
        bridge.stdin.write(json.dumps(dict(jsonrpc='2.0',id=number,method=method,params=params),ensure_ascii=False)+'\n'); bridge.stdin.flush()
        while True:
            value=lines.get(timeout=30)
            if value.get('id')==number:
                assert 'result' in value,value
                return value['result']
            events.append(value)
    def tool(name, **arguments):
        result=request('tools/call',dict(name=name,arguments=arguments)); assert not result.get('isError'),result; return result
    def content(name, **arguments): return json.loads(tool(name,**arguments)['content'][0]['text'])
    init=request('initialize',dict(protocolVersion='2025-11-25',capabilities={},clientInfo=dict(name='Linux verification',version='1')))
    bridge.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n'); bridge.stdin.flush()
    check(init['serverInfo']['version']=='1.0.0','real stdio bridge negotiates version')
    check(len(request('tools/list')['tools'])==9,'all MCP tools available')
    check(len(content('list_actions'))==106,'same 106 production actions')
    text='主人，Linux 端收到的原文呀~ ABC 123'
    content('say',text=text); wait(lambda: state()['text']==text)
    check(state()['modelPid'] is None and state()['bubbleVisible'],'literal speech works without local model')
    content('interrupt_action',action='点击回应-元气挥手')
    content('play_action',action='吃白饭')
    wait(lambda: state()['currentAction']=='点击回应-元气挥手',3)
    wait(lambda: state()['currentAction']=='吃白饭',14)
    check(True,'queued animation begins after actual decoder completion')
    wait(lambda: state()['phase']=='PuppetIdle',14)
    check(True,'external action returns to fixed idle')
    content('say_next',text='下次自动节奏')
    command('speech-test'); time.sleep(2)
    check(state()['text']==text and state()['modelPid'] is None,'puppet rejects speech test and does not consume scheduled speech')
    old=state(); x,y=old['X']+250,old['Y']+130
    subprocess.run(['xdotool','mousemove',str(x),str(y),'click','1'],check=True,env=env)
    time.sleep(.3)
    subprocess.run(['xdotool','mousemove',str(x),str(y),'mousedown','1','mousemove',str(x-40),str(y-20),'sleep','.2','mouseup','1'],check=True,env=env)
    time.sleep(1)
    content('get_settings')
    kinds=[e.get('params',{}).get('data',{}).get('kind') for e in events]
    check(all(k in kinds for k in ['click','drag_start','drag_end']),'real pointer click and drag pushed automatically')
    check(state()['currentAction']=='待机呼吸休闲','pointer gestures do not switch puppet animation')
    content('update_settings',settings={'McpChatEnabled':True})
    location=state(); x,y=location['X']+250,location['Y']+130
    subprocess.run(['xdotool','mousemove',str(x),str(y),'click','--repeat','2','--delay','120','1'],check=True,env=env)
    wait(lambda:content('get_settings')['state']['inputOpen'],5)
    def input_window():
        # Find the real top-level input by process and size; xdotool's title regex depends on locale for CJK text.
        search=subprocess.run(['xdotool','search','--onlyvisible','--pid',str(app.pid)],env=env,text=True,capture_output=True)
        for window in search.stdout.splitlines():
            geometry=subprocess.run(['xdotool','getwindowgeometry','--shell',window],env=env,text=True,capture_output=True)
            values=dict(line.split('=',1) for line in geometry.stdout.splitlines() if '=' in line)
            if int(values.get('WIDTH',0))>=500 and int(values.get('HEIGHT',0))>=400:return window
        return None
    entry=wait(input_window,5)
    check(bool(entry),'actual double-click opens input only after enabling connected MCP chat')
    subprocess.run(['xdotool','windowactivate','--sync',entry],check=True,env=env)
    message='Hello fish from Linux 123 & <>'
    subprocess.run(['xdotool','type','--clearmodifiers','--delay','3',message],check=True,env=env)
    command('render-input');wait(lambda:(data/'input.png').exists())
    subprocess.run(['xdotool','key','ctrl+Return'],check=True,env=env);time.sleep(.4)
    content('get_settings')
    submitted=[e['params']['data']['detail']['text'] for e in events if e.get('params',{}).get('data',{}).get('kind')=='user_message']
    if message not in submitted:print('Submitted test messages:',repr(submitted),flush=True)
    check(message in submitted,'actual keyboard input travels over MCP unchanged')
    subprocess.run(['xdotool','key','alt+F4'],check=True,env=env)
    # Foreground fixture is a separate SDL application, not a title injected into the fish.
    fixture=subprocess.Popen(['/usr/bin/ffplay','-loglevel','error','-window_title','BigBlueFish Development','-f','lavfi','-i',"color=c=white:s=800x500:r=1,drawtext=text='Developing BigBlueFish Project':fontcolor=black:fontsize=36:x=40:y=100"],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    time.sleep(2)
    found=subprocess.check_output(['xdotool','search','--name','^BigBlueFish Development$'],env=env,text=True).splitlines()[0]
    subprocess.run(['xdotool','windowactivate','--sync',found],check=True,env=env); time.sleep(.5)
    for name in ['capture_foreground','capture_screen']:
        result=tool(name,max_dimension=1280); image=base64.b64decode(result['content'][1]['data']); metadata=json.loads(result['content'][0]['text'])
        check(result['content'][1]['mimeType']=='image/png' and image[:8]==b'\x89PNG\r\n\x1a\n',name+' actual PNG')
        (data / (name+'.png')).write_bytes(image)
        if name=='capture_foreground': check('BigBlueFish Development' in metadata['identity'],'foreground selected from real external SDL window')
    command('ocr'); wait(lambda: (data/'ocr-status.json').exists(),30)
    ocr=json.loads((data/'ocr-status.json').read_text())
    check('BigBlueFish' in ocr['Context'] and ocr['WordCount']>2,'actual tesseract OCR reads foreground pixels')
    command('render'); wait(lambda: (data/'pet.png').exists())
    command('settings'); wait(lambda: state()['settingsOpen'])
    command('render-settings'); wait(lambda: (data/'settings.png').exists())
    settings_windows=subprocess.check_output(['xdotool','search','--onlyvisible','--pid',str(app.pid)],env=env,text=True).splitlines()
    subprocess.run(['xdotool','windowactivate','--sync',found],check=True,env=env)
    content('update_settings',settings={'BubbleScale':1.35,'AnimationIntervalSeconds':65})
    time.sleep(.4)
    after_windows=subprocess.check_output(['xdotool','search','--onlyvisible','--pid',str(app.pid)],env=env,text=True).splitlines()
    active=subprocess.check_output(['xdotool','getactivewindow'],env=env,text=True).strip()
    check(set(settings_windows)==set(after_windows) and active==found and state()['settingsOpen'], 'MCP refresh keeps the same Linux settings window without taking foreground')
    command('close-settings'); wait(lambda: not state()['settingsOpen'])
    content('update_settings',settings={'PuppetMode':False,'BubbleScale':1.2})
    wait(lambda:not state()['Puppet'])
    check(json.loads((data/'preferences.json').read_text())['BubbleScale']==1.2,'settings saved and puppet disabled explicitly')
    prior_windows=set(subprocess.check_output(['xdotool','search','--onlyvisible','--pid',str(app.pid)],env=env,text=True).splitlines())
    request_id+=1; consent_id=request_id
    bridge.stdin.write(json.dumps(dict(jsonrpc='2.0',id=consent_id,method='tools/call',params=dict(name='update_settings',arguments=dict(settings={'PuppetMode':True}))))+'\n');bridge.stdin.flush()
    def consent_window():
        windows=set(subprocess.check_output(['xdotool','search','--onlyvisible','--pid',str(app.pid)],env=env,text=True).splitlines())
        extra=windows-prior_windows
        return next(iter(extra)) if extra else None
    notice=wait(consent_window,6); before_drag=state(); x,y=before_drag['X']+250,before_drag['Y']+130
    subprocess.run(['xdotool','mousemove',str(x),str(y),'mousedown','1','mousemove',str(x-40),str(y-20),'sleep','.2','mouseup','1'],check=True,env=env)
    # Status is written once a second; wait for the moved window to appear in it.
    def moved_during_consent():
        current=state()
        return current if (current['X'],current['Y'])!=(before_drag['X'],before_drag['Y']) else None
    after_drag=wait(moved_during_consent,3)
    check((after_drag['X'],after_drag['Y'])!=(before_drag['X'],before_drag['Y']) and not after_drag['Puppet'],'real Linux pet remains draggable while mode consent is pending')
    subprocess.run(['xdotool','windowactivate','--sync',notice,'key','alt+F4'],check=True,env=env)
    def cancelled_response():
        request('ping')
        return next((e for e in events if e.get('id')==consent_id),None)
    cancelled=wait(cancelled_response,5)
    check(cancelled.get('result',{}).get('isError') and not state()['Puppet'],'closing pending notice cancels mode change without disabling pet')
    content('update_settings',settings={'Left':420,'Top':220})
    wait(lambda:abs(state()['X']-420)<2 and abs(state()['Y']-220)<2)
    check(True,'MCP coordinate settings move the actual Linux window')
    check(not (data/'test-error.txt').exists(),'no application diagnostic errors')
finally:
    if bridge: bridge.terminate(); bridge.wait(timeout=5)
    if fixture: fixture.terminate(); fixture.wait(timeout=5)
    if app.poll() is None:
        command('exit')
        try: app.wait(timeout=5)
        except subprocess.TimeoutExpired: app.kill(); app.wait()
    log.close()
if '--safe' not in sys.argv:
    saved=json.loads((data/'preferences.json').read_text())
    check(saved['Left']==420 and saved['Top']==220,'actual Linux position persists after native window shutdown')
    print(f'Linux actual integration: {count} checks passed.',flush=True)
