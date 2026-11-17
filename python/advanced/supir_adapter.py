"""Official SUPIR pipeline without LLaVA or web UI. Q/F select real checkpoints."""
import os
import sys
import types
from catalog import asset_path

def infer(image, req, config, work, progress):
    import torch
    upstream = asset_path({'path': config['upstream']})
    sys.path.insert(0, str(upstream))
    os.chdir(upstream)
    assets = config['assets']
    paths = types.ModuleType('CKPT_PTH')
    paths.SDXL_CLIP1_PATH = str(asset_path(assets['clip_config']).parent)
    paths.SDXL_CLIP2_CKPT_PTH = str(asset_path(assets['openclip']))
    sys.modules['CKPT_PTH'] = paths
    from omegaconf import OmegaConf
    from sgm.util import instantiate_from_config
    from SUPIR.util import load_state_dict, PIL2Tensor, Tensor2PIL
    progress('loading')
    cfg = OmegaConf.load(upstream / 'options/SUPIR_v0.yaml')
    cfg.model.params.ae_dtype = 'bf16' if torch.cuda.is_bf16_supported() else 'fp32'
    model = instantiate_from_config(cfg.model).cpu()
    model.load_state_dict(load_state_dict(str(asset_path(assets['sdxl']))), strict=False)
    model.load_state_dict(load_state_dict(str(asset_path(assets['checkpoint']))), strict=False)
    opt = req['options']
    model = model.half()
    if opt['tiled']:
        model.init_tile_vae(encoder_tile_size=512, decoder_tile_size=64)
    model.ae_dtype = torch.bfloat16 if torch.cuda.is_bf16_supported() else torch.float32
    model.model.dtype = torch.float16
    model = model.to('cuda').eval()
    tensor, h, w = PIL2Tensor(image.convert('RGB'), upsacle=req['scale'], min_size=1024)
    tensor = tensor.unsqueeze(0).to('cuda')
    progress('stage2')
    with torch.no_grad():
        samples = model.batchify_sample(tensor, [''], num_steps=opt['steps'], restoration_scale=-1,
            s_churn=5, s_noise=1.003, cfg_scale=opt['cfg'], control_scale=opt['strength'],
            seed=opt['seed'], num_samples=1, p_p=opt['positive'], n_p=opt['negative'],
            color_fix_type=opt['color_fix'], use_linear_CFG=True, use_linear_control_scale=False,
            cfg_scale_start=1.0, control_scale_start=0.0)
    return Tensor2PIL(samples[0], h, w)
