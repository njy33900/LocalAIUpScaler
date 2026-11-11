import json, os, sys, time
from pathlib import Path
sys.stdin.reconfigure(encoding='utf-8');sys.stdout.reconfigure(encoding='utf-8')
def emit(kind, job='', **values):
    print(json.dumps(dict(protocol_version=1,type=kind,job_id=job,**values)),flush=True)
emit('ready',cuda=False)
for line in sys.stdin:
    req=json.loads(line); kind=req['type']
    if kind=='shutdown': break
    if kind=='upscale':
        path=Path(req['input']); job=req['job_id']
        if path.stem in ('hang','crash'):
            (Path(req['output_dir'])/('.upscaler-'+job+'.tmp')).write_bytes(b'partial')
            emit('progress',job,stage='inference',progress=5)
            if path.stem=='crash': os._exit(7)
            time.sleep(3600)
        emit('completed',job,output=str(Path(req['output_dir'])/'result.png'))
