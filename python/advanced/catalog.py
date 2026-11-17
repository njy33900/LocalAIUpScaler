"""Pinned upstreams and offline runtime manifest. No imports of ML packages here."""
import hashlib
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNTIME = ROOT / '.advanced'
CONFIG = RUNTIME / 'models.json'
REVISIONS = {'supir': 'bda91af2000042f8bedfec8897d92917e67c1d88',
             'diffbir': '5c2d6c177c3f2965e6d33a32931248d242ef7b3d'}
ENGINES = {'supir_v0q': 'supir', 'supir_v0f': 'supir', 'diffbir_v21': 'diffbir'}

def load_config():
    return json.loads(CONFIG.read_text(encoding='utf-8')) if CONFIG.exists() else {}

def asset_path(asset):
    p = Path(asset['path'])
    return p if p.is_absolute() else ROOT / p

def fingerprint(config):
    return hashlib.sha256(json.dumps(config, sort_keys=True).encode()).hexdigest()

def inspect(engine, deep=False):
    if engine not in ENGINES:
        raise ValueError('Unknown advanced engine')
    config = load_config().get(engine, {})
    missing = []
    backend = ENGINES[engine]
    for key in ('python', 'upstream'):
        if not config.get(key) or not asset_path({'path': config[key]}).exists():
            missing.append(key + ': ' + config.get(key, 'not configured'))
    if config.get('upstream') and asset_path({'path': config['upstream']}).is_dir():
        try:
            directory=str(asset_path({'path': config['upstream']}))
            command=['git','-c','safe.directory='+directory,'-C',directory]
            revision=subprocess.run(command+['rev-parse','HEAD'],stdin=subprocess.DEVNULL,capture_output=True,text=True,timeout=8)
            changes=subprocess.run(command+['status','--porcelain','--untracked-files=no'],stdin=subprocess.DEVNULL,capture_output=True,text=True,timeout=8)
            if revision.returncode or revision.stdout.strip()!=REVISIONS[backend] or changes.returncode or changes.stdout.strip():
                missing.append('upstream revision mismatch or modified files')
        except (OSError,subprocess.TimeoutExpired):
            missing.append('upstream revision check failed')
    dependency_state = None
    if config.get('python') and asset_path({'path':config['python']}).is_file():
        try:
            probe=subprocess.run([str(asset_path({'path':config['python']})),str(ROOT/'python/advanced/runtime_probe.py'),backend],
                stdin=subprocess.DEVNULL,capture_output=True,text=True,encoding='utf-8',timeout=10)
            data=json.loads(probe.stdout) if probe.returncode==0 else {'missing':['probe failed'],'incompatible':[]}
            if data['missing']:
                dependency_state='missing_dependencies'
                missing.extend('dependency missing: '+name for name in data['missing'])
            if data['incompatible']:
                dependency_state='incompatible'
                missing.extend('dependency incompatible: '+name for name in data['incompatible'])
        except (OSError,ValueError,subprocess.TimeoutExpired):
            dependency_state='failed_healthcheck'
            missing.append('dependency probe failed')
    required = ['cleaner', 'sd', 'control'] if backend == 'diffbir' else ['checkpoint', 'sdxl', 'clip_config', 'clip_model', 'clip_vocab', 'clip_merges', 'openclip']
    assets = config.get('assets', {})
    for key in required:
        asset = assets.get(key, {})
        if not asset.get('path') or not asset_path(asset).is_file():
            missing.append(key + ': ' + asset.get('path', 'not configured'))
        elif not asset.get('sha256'):
            missing.append(key + ': SHA256 missing')
        elif deep:
            with asset_path(asset).open('rb') as f:
                actual = hashlib.file_digest(f, 'sha256').hexdigest()
            if actual != asset['sha256']:
                missing.append(key + ': SHA256 mismatch')
    receipt = RUNTIME / (engine + '.verified.json')
    verified = False
    verified_scales = []
    if receipt.exists() and not missing:
        try:
            r = json.loads(receipt.read_text(encoding='utf-8'))
            verified = r['fingerprint'] == fingerprint(config) and all(
                [asset_path(assets[k]).stat().st_size, asset_path(assets[k]).stat().st_mtime_ns] == v
                for k, v in r['asset_stats'].items())
            if verified:
                verified_scales = r.get('verified_scales', [])
        except (ValueError, KeyError, OSError):
            verified = False
    return {'engine': engine, 'state': (dependency_state or ('invalid' if any('mismatch' in m for m in missing) else 'not_installed')) if missing else ('ready' if verified else 'failed_healthcheck'),
            'missing': missing, 'verified_inference': verified, 'verified_scales': verified_scales,
            'message': '\n'.join(missing) if missing else ('Actual inference verified' if verified else 'Run import check and actual smoke inference before enabling'),
            'config': config}

def receipt(engine, scale):
    config = load_config()[engine]
    path = RUNTIME / (engine + '.verified.json')
    previous = json.loads(path.read_text(encoding='utf-8')).get('verified_scales', []) if path.exists() else []
    stats = {k: [asset_path(a).stat().st_size, asset_path(a).stat().st_mtime_ns]
             for k, a in config['assets'].items()}
    (RUNTIME / (engine + '.verified.json')).write_text(json.dumps(
        {'fingerprint': fingerprint(config), 'asset_stats': stats, 'actual_inference': True, 'verified_scales': sorted(set(previous + [scale]))}, indent=2), encoding='utf-8')
