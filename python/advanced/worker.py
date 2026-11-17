"""JSONL supervisor + one-shot inference process; model memory dies after every job."""
import argparse
import contextlib
import json
import math
import os
from pathlib import Path
import re
import secrets
import shutil
import subprocess
import sys
import tempfile
import threading
import traceback
from catalog import ROOT, ENGINES, inspect, load_config, receipt

LOCK = threading.Lock()
ACTIVE = None
PROTOCOL = sys.stdout

def emit(kind, job='', **data):
    with LOCK:
        print(json.dumps(dict(protocol_version=1, type=kind, job_id=job, **data), ensure_ascii=False), file=PROTOCOL, flush=True)

def validate(req):
    if req.get('protocol_version') != 1 or req.get('type') != 'restore':
        raise ValueError('Invalid protocol or command')
    if not re.fullmatch(r'[a-zA-Z0-9_-]{1,80}', req.get('job_id', '')):
        raise ValueError('Invalid job_id')
    if req.get('engine') not in ENGINES or req.get('device') != 'cuda':
        raise ValueError('Advanced restoration requires a registered engine and CUDA')
    if req.get('task', 'restore') not in ('restore', 'sr') or req.get('scale') not in (1, 2, 4):
        raise ValueError('Unsupported task or scale')
    if req.get('format') not in ('PNG', 'JPEG'):
        raise ValueError('Unsupported output format')
    opt = req.get('options', {})
    defaults = dict(steps=10 if req['engine']=='diffbir_v21' else 50, cfg=6 if req['engine']=='diffbir_v21' else 4,
                    strength=1.0, noise_aug=0, seed=-1, positive='', negative='', color_fix='Wavelet', tiled=True)
    if set(opt) - set(defaults):
        raise ValueError('Unknown options: ' + ', '.join(set(opt)-set(defaults)))
    defaults.update(opt)
    for key, lo, hi in [('steps',1,100), ('cfg',1,20), ('strength',0,2), ('noise_aug',0,1000), ('seed',-1,2147483647)]:
        v=defaults[key]
        if isinstance(v,bool) or not isinstance(v,(int,float)) or not math.isfinite(v) or not lo<=v<=hi:
            raise ValueError('Invalid option: '+key)
    for key in ('steps','seed','noise_aug'):
        if not isinstance(defaults[key],int):
            raise ValueError('Integer required: '+key)
    if defaults['color_fix'] not in ('None','AdaIn','Wavelet') or not isinstance(defaults['tiled'],bool):
        raise ValueError('Invalid color fix / tiling')
    for key in ('positive','negative'):
        if not isinstance(defaults[key],str) or len(defaults[key])>4000:
            raise ValueError('Invalid prompt')
    if req['engine']=='diffbir_v21' and (defaults['strength']!=1 or defaults['color_fix']!='Wavelet'):
        raise ValueError('SUPIR-only options in DiffBIR request')
    if req['engine']!='diffbir_v21' and defaults['noise_aug']!=0:
        raise ValueError('DiffBIR-only noise augmentation')
    if defaults['seed']==-1:
        defaults['seed']=secrets.randbelow(2147483647)
    for key in ('input','output_dir'):
        if not Path(req.get(key,'')).is_absolute():
            raise ValueError('Absolute '+key+' required')
    req['options']=defaults
    return req

def run_job(req, work):
    req=validate(req)
    state=inspect(req['engine'], deep=True)
    if state['missing']:
        raise FileNotFoundError(state['message'])
    if not req.get('smoke') and (not state['verified_inference'] or req['scale'] not in state['verified_scales']):
        raise RuntimeError('Engine/scale requires successful actual smoke inference')
    config=state['config']
    from offline import enforce
    enforce()
    sys.path.insert(0,str(ROOT/'python'))
    from imaging import read_image
    from PIL import Image
    import torch
    if not torch.cuda.is_available():
        raise RuntimeError('CUDA_UNAVAILABLE')
    stage=lambda name: emit('progress',req['job_id'],stage=name,progress=None)
    image=read_image(req['input'])
    alpha=image.getchannel('A') if image.mode=='RGBA' else None
    # Composite transparent inputs on white for models trained on RGB.
    rgb=Image.new('RGB',image.size,'white')
    rgb.paste(image,mask=alpha)
    adapter=__import__('diffbir_adapter' if req['engine']=='diffbir_v21' else 'supir_adapter')
    # All third-party stdout is diagnostic, never the supervisor protocol.
    with contextlib.redirect_stdout(sys.stderr):
        output=adapter.infer(rgb,req,config,work,stage)
    expected=(image.width*req['scale'],image.height*req['scale'])
    if output.size!=expected:
        raise ValueError(f'Output dimensions {output.size} do not match {expected}')
    if alpha is not None and req['format']=='PNG':
        output=output.convert('RGBA')
        output.putalpha(alpha.resize(output.size,Image.Resampling.BILINEAR))
    stage('saving')
    dest=Path(req['output_dir']);dest.mkdir(parents=True,exist_ok=True)
    temp=dest/('.upscaler-'+req['job_id']+'.tmp')
    try:
        with temp.open('xb') as f:
            output.convert('RGB' if req['format']=='JPEG' else output.mode).save(f,format=req['format'],quality=95)
            f.flush();os.fsync(f.fileno())
        with Image.open(temp) as saved:
            saved.verify()
        suffix='.jpg' if req['format']=='JPEG' else '.png'
        stem=Path(req['input']).stem[:100]+'_'+req['engine']+'_x'+str(req['scale'])
        n=0
        while True:
            target=dest/(stem+('' if not n else f'_{n:03d}')+suffix)
            try:
                # Windows rename is atomic and refuses existing destinations.
                os.rename(temp,target)
                break
            except FileExistsError:
                n+=1
        if req.get('smoke'):
            receipt(req['engine'], req['scale'])
        metadata=dict(engine=req['engine'],preset=req.get('preset','custom'),options=req['options'],scale=req['scale'],
                      revision=config['revision'],assets={k:a['sha256'] for k,a in config['assets'].items()})
        # Job metadata is local; images are never embedded.
        log=ROOT/'artifacts/advanced-jobs';log.mkdir(parents=True,exist_ok=True)
        (log/(req['job_id']+'.json')).write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf-8')
        emit('completed',req['job_id'],output=str(target),output_width=output.width,output_height=output.height,device='cuda',precision='fp16')
    finally:
        temp.unlink(missing_ok=True)

@contextlib.contextmanager
def staging_directory(req):
    output=Path(req['output_dir'])
    output.mkdir(parents=True,exist_ok=True)
    directory=output/('.restoration-'+req['job_id'])
    directory.mkdir()
    try:
        yield directory
    finally:
        shutil.rmtree(directory,ignore_errors=False)


def supervise(req, cancel):
    global ACTIVE
    terminal = None
    try:
        with staging_directory(req) as temp:
            work=Path(temp); f=work/'request.json'
            f.write_text(json.dumps(req),encoding='utf-8')
            with subprocess.Popen([sys.executable,'-u',str(Path(__file__).resolve()),'--job',str(f)],
                    stdin=subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=sys.stderr,text=True,encoding='utf-8') as child:
                ACTIVE['child']=child
                if cancel.is_set():
                    child.kill()
                for line in child.stdout:
                    message=json.loads(line)
                    if message['type'] in ('completed','failed'):
                        terminal=message
                    else:
                        emit(message['type'],message['job_id'],**{k:v for k,v in message.items() if k not in ('protocol_version','type','job_id')})
                code=child.wait()
                if terminal is None:
                    terminal=dict(type='cancelled' if cancel.is_set() else 'failed',job_id=req['job_id'],
                                  code='WORKER_CRASH',message=f'Inference process exited: {code}')
    except Exception as error:
        traceback.print_exc(file=sys.stderr)
        terminal=dict(type='failed',job_id=req.get('job_id',''),code=type(error).__name__,message=str(error))
    finally:
        try:
            temp=Path(req['output_dir'])/('.upscaler-'+req['job_id']+'.tmp')
            temp.unlink(missing_ok=True)
        except OSError:
            traceback.print_exc(file=sys.stderr)
        # The next folder job is allowed only after the child and staging files are gone.
        ACTIVE=None
    emit(terminal['type'],terminal['job_id'],**{k:v for k,v in terminal.items() if k not in ('protocol_version','type','job_id')})

def main():
    global ACTIVE
    sys.stdin.reconfigure(encoding='utf-8')
    import torch
    emit('ready',cuda=torch.cuda.is_available(),engine='advanced',scale=None)
    for line in sys.stdin:
        req={}
        try:
            req=json.loads(line);kind=req.get('type')
            if kind in ('cancel','shutdown'):
                active=ACTIVE
                if active and (kind=='shutdown' or req.get('job_id')==active['id']):
                    active['cancel'].set()
                    child=active.get('child')
                    if child and child.poll() is None:
                        child.kill();child.wait()
                    active['thread'].join()
                if kind=='shutdown':
                    break
            elif kind=='restore':
                validate(req)
                if ACTIVE:
                    raise RuntimeError('Worker busy')
                cancel=threading.Event()
                thread=threading.Thread(target=supervise,args=(req,cancel))
                ACTIVE=dict(id=req['job_id'],cancel=cancel,thread=thread)
                thread.start()
            elif kind=='hello':
                emit('device_info',req.get('job_id',''),cuda=True)
            else:
                raise ValueError('Unknown command')
        except Exception as error:
            emit('failed',req.get('job_id',''),code=type(error).__name__,message=str(error))
    active=ACTIVE
    if active:
        child=active.get('child')
        if child and child.poll() is None:
            child.kill();child.wait()
        active['thread'].join()

if __name__=='__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')
    parser=argparse.ArgumentParser();parser.add_argument('--job');parser.add_argument('--health')
    args=parser.parse_args()
    if args.health:
        print(json.dumps(inspect(args.health),ensure_ascii=False))
    elif args.job:
        request=json.loads(Path(args.job).read_text(encoding='utf-8'))
        try:
            run_job(request,Path(args.job).parent)
        except Exception as error:
            traceback.print_exc(file=sys.stderr)
            emit('failed',request['job_id'],code='CUDA_OOM' if 'out of memory' in str(error).lower() else type(error).__name__,message=str(error))
            sys.exit(1)
    else:
        main()
