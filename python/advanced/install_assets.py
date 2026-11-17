"""Explicit installation only; never imported by inference workers."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path
import time
import urllib.request
from catalog import ROOT, RUNTIME, CONFIG, REVISIONS, asset_path, inspect

HF = {
    'diffbir': ('lxq007/DiffBIR-v2', {'cleaner':'realesrgan_s4_swinir_100k.pth', 'sd':'sd2.1-base-zsnr-laionaes5.ckpt','control':'DiffBIR_v2.1.pt'}),
    'sdxl': ('stabilityai/stable-diffusion-xl-base-1.0', {'sdxl':'sd_xl_base_1.0_0.9vae.safetensors'}),
    'clip': ('openai/clip-vit-large-patch14', {'clip_config':'config.json','clip_model':'pytorch_model.bin','clip_vocab':'vocab.json','clip_merges':'merges.txt','clip_tokenizer':'tokenizer_config.json','clip_special':'special_tokens_map.json'}),
    'openclip': ('laion/CLIP-ViT-bigG-14-laion2B-39B-b160k', {'openclip':'open_clip_pytorch_model.bin'}),
}

def metadata(group):
    repo, files = HF[group]
    with urllib.request.urlopen('https://huggingface.co/api/models/'+repo+'?blobs=true',timeout=45) as f:
        info=json.load(f)
    siblings={f['rfilename']:f for f in info['siblings']}
    assets={}
    for key, filename in files.items():
        data=siblings[filename];lfs=data.get('lfs',{})
        assets[key]=dict(path=f'models/advanced/{group}/{filename}',filename=filename,
                         sha256=lfs.get('sha256'),git_blob=data.get('blobId'),size=lfs.get('size'),
                         url=f"https://huggingface.co/{repo}/resolve/{info['sha']}/{filename}",
                         repository=repo,revision=info['sha'])
    return assets

def download(asset):
    target=asset_path(asset);target.parent.mkdir(parents=True,exist_ok=True)
    if target.exists() and asset.get('sha256'):
        with target.open('rb') as f:
            if hashlib.file_digest(f,'sha256').hexdigest()==asset['sha256']:
                print('VERIFIED cached',target.name,flush=True);return
    part=target.with_suffix(target.suffix+'.download')
    offset=part.stat().st_size if part.exists() else 0
    headers={'User-Agent':'LocalAIUpscaler-Installer/1.3'}
    if offset: headers['Range']=f'bytes={offset}-'
    request=urllib.request.Request(asset['url'],headers=headers)
    print('DOWNLOAD',target.name,'resume',offset,flush=True)
    with urllib.request.urlopen(request,timeout=90) as response:
        append=offset and response.status==206
        with part.open('ab' if append else 'wb') as f:
            last=time.monotonic()
            while chunk:=response.read(4*1024*1024):
                f.write(chunk)
                if time.monotonic()-last>15:
                    print(target.name,round(f.tell()/1e9,2),'GB',flush=True);last=time.monotonic()
    with part.open('rb') as f: sha=hashlib.file_digest(f,'sha256').hexdigest()
    if asset.get('sha256') and sha!=asset['sha256']:
        raise ValueError('SHA256 mismatch: '+target.name)
    if not asset.get('sha256') and asset.get('git_blob'):
        data=part.read_bytes()
        blob=hashlib.sha1(f'blob {len(data)}\0'.encode()+data).hexdigest()
        if blob!=asset['git_blob']: raise ValueError('Git blob mismatch: '+target.name)
    asset['sha256']=sha
    part.replace(target)
    print('VERIFIED',target.name,sha,flush=True)

def configure(engine, do_download):
    RUNTIME.mkdir(exist_ok=True)
    config=json.loads(CONFIG.read_text(encoding='utf-8')) if CONFIG.exists() else {}
    backend='diffbir' if engine=='diffbir_v21' else 'supir'
    lock=ROOT/'config/advanced-models.lock.json'
    template=json.loads(lock.read_text(encoding='utf-8')).get(engine,{}) if lock.exists() else {}
    assets=config.get(engine,{}).get('assets') or template.get('assets') or (metadata('diffbir') if backend=='diffbir' else {**metadata('sdxl'),**metadata('clip'),**metadata('openclip')})
    if do_download:
        with ThreadPoolExecutor(max_workers=2) as pool:
            list(pool.map(download,[a for key,a in assets.items() if key!='checkpoint']))
    if backend=='supir':
        # Official Q/F Drive file IDs are filled by the installer after folder inventory.
        name='SUPIR-v0Q.ckpt' if engine=='supir_v0q' else 'SUPIR-v0F.ckpt'
        receipts=ROOT/'models/advanced/supir/download-receipts.json'
        downloaded=json.loads(receipts.read_text(encoding='utf-8')).get(name,{}) if receipts.exists() else {}
        previous=downloaded or config.get(engine,{}).get('assets',{}).get('checkpoint',{}) or template.get('assets',{}).get('checkpoint',{})
        assets['checkpoint']=previous or dict(path='models/advanced/supir/'+name, filename=name,sha256=None,
            url='https://drive.google.com/drive/folders/1yELzm5SvAi9e7kPcO_jPp2XkTs4vK6aR')
    entry=dict(python=f'.advanced/env-{backend}/Scripts/python.exe',upstream=f'artifacts/advanced-upstream/{backend}',
               revision=REVISIONS[backend],assets=assets)
    config[engine]=entry
    CONFIG.write_text(json.dumps(config,indent=2),encoding='utf-8')
    print('Configured',engine,flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('engine',choices=['diffbir_v21','supir_v0q','supir_v0f']);parser.add_argument('--download',action='store_true');parser.add_argument('--verify',action='store_true')
    args=parser.parse_args()
    if args.verify:
        state=inspect(args.engine,deep=True)
        print(json.dumps(state,ensure_ascii=False))
        if state['missing']: raise SystemExit(1)
    else:
        configure(args.engine,args.download)
