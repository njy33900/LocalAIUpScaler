"""Real weights only. An actual output is required before a ready receipt is written."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
import time
import uuid
from catalog import ROOT, load_config, asset_path

def smoke(engine):
    from PIL import Image, ImageDraw
    config=load_config()[engine]
    directory=ROOT/'artifacts/advanced-smoke'/engine
    directory.mkdir(parents=True,exist_ok=True)
    image=Image.new('RGB',(256,256),'#9aa7b1')
    draw=ImageDraw.Draw(image)
    draw.rectangle((30,30,220,220),fill='#607d8b')
    draw.ellipse((70,55,190,175),fill='#d6ba9d')
    draw.line((10,235,245,10),fill='white',width=2)
    inp=directory/'입력 sample.png';image.save(inp)
    job=uuid.uuid4().hex
    request=dict(protocol_version=1,type='restore',job_id=job,engine=engine,device='cuda',scale=2,
                 input=str(inp),output_dir=str(directory),format='PNG',options={},smoke=True)
    with tempfile.TemporaryDirectory(prefix='advanced-smoke-') as temp:
        p=Path(temp)/'request.json';p.write_text(json.dumps(request),encoding='utf-8')
        with (directory/'events.jsonl').open('w',encoding='utf-8') as out, (directory/'stderr.log').open('w',encoding='utf-8') as err:
            child=subprocess.Popen([str(asset_path({'path':config['python']})),'-u',str(ROOT/'python/advanced/worker.py'),'--job',str(p)],
                stdin=subprocess.DEVNULL,stdout=out,stderr=err,cwd=ROOT)
            try:
                code=child.wait(timeout=900)
            except subprocess.TimeoutExpired:
                subprocess.run(['taskkill','/PID',str(child.pid),'/T','/F'],capture_output=True,timeout=15)
                child.wait();raise
    print(engine,'exit',code,'logs',directory,flush=True)
    if code:
        raise SystemExit(code)
    events=[json.loads(x) for x in (directory/'events.jsonl').read_text(encoding='utf-8').splitlines()]
    result=next((e for e in events if e['type']=='completed'),None)
    if result is None:
        raise RuntimeError('No actual completion event')
    with Image.open(result['output']) as output:
        assert output.size==(512,512)
        output.verify()
    print(json.dumps(result,ensure_ascii=False),flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('engine');args=parser.parse_args();smoke(args.engine)
