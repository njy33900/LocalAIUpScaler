"""Headless invocation of pinned official v2.1 SR (SwinIR + CLDM)."""
import os
from pathlib import Path
import runpy
import sys
from catalog import asset_path

def infer(image, req, config, work, progress):
    import torch
    upstream = asset_path({'path': config['upstream']})
    sys.path.insert(0, str(upstream))
    os.chdir(upstream)
    from diffbir.utils import common
    assets = config['assets']
    mapping = {a['filename']: asset_path(a) for a in assets.values()}
    def local_load(url):
        name = url.rsplit('/', 1)[-1]
        if name not in mapping:
            raise FileNotFoundError('Unregistered upstream weight: ' + name)
        state = torch.load(mapping[name], map_location='cpu')
        state = state.get('state_dict', state)
        if next(iter(state)).startswith('module.'):
            state = {k[len('module.'):]: v for k, v in state.items()}
        return state
    common.load_model_from_url = local_load
    from diffbir.pipeline import Pipeline
    # Hook real stage entry points; these are indeterminate, not fabricated percentages.
    for method, stage in [('apply_cldm', 'stage2')]:
        original = getattr(Pipeline, method)
        def wrapped(self, *args, _original=original, _stage=stage, **kwargs):
            progress(_stage)
            return _original(self, *args, **kwargs)
        setattr(Pipeline, method, wrapped)
    from diffbir.pipeline import SwinIRPipeline
    original_cleaner = SwinIRPipeline.apply_cleaner
    def cleaner(self, *args, **kwargs):
        progress('stage1')
        return original_cleaner(self, *args, **kwargs)
    SwinIRPipeline.apply_cleaner = cleaner
    inp, out = work / 'input', work / 'output'
    inp.mkdir(); out.mkdir()
    image.convert('RGB').save(inp / 'input.png')
    opt = req['options']
    sys.argv = ['inference.py', '--task', 'sr', '--version', 'v2.1', '--captioner', 'none',
                '--input', str(inp), '--output', str(out), '--upscale', str(req['scale']),
                '--device', 'cuda', '--precision', 'fp16', '--steps', str(opt['steps']),
                '--sampler', 'edm_dpm++_3m_sde', '--cfg_scale', str(opt['cfg']),
                '--seed', str(opt['seed']), '--noise_aug', str(opt['noise_aug']),
                '--pos_prompt', opt['positive'], '--neg_prompt', opt['negative']]
    if opt['tiled']:
        sys.argv += ['--cleaner_tiled', '--vae_encoder_tiled', '--vae_decoder_tiled', '--cldm_tiled']
    progress('loading')
    runpy.run_path(str(upstream / 'inference.py'), run_name='__main__')
    from PIL import Image
    with Image.open(out / 'input.png') as result:
        return result.copy()
