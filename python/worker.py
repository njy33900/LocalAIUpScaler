"""UTF-8 JSONL worker. stdout is exclusively protocol data."""
import gc
import hashlib
import json
import os
import queue
import re
import shutil
import sys
import threading
import traceback
from pathlib import Path

import numpy as np
from PIL import Image
import torch
from rrdbnet_arch import RRDBNet
from imaging import read_image
from engine.base_engine import BaseEngine

ROOT = Path(__file__).resolve().parents[1]
QUEUE = queue.Queue(maxsize=1)
WRITE_LOCK = threading.Lock()
STATE_LOCK = threading.Lock()
ACTIVE = None
MODEL = None
MODEL_KEY = None

def emit(kind, job='', **data):
    with WRITE_LOCK:
        print(json.dumps(dict(protocol_version=1, type=kind, job_id=job, **data), ensure_ascii=False), flush=True)

def device_info():
    return dict(cuda=torch.cuda.is_available(), devices=[torch.cuda.get_device_name(i) for i in range(torch.cuda.device_count())],
                engine_id='realesrgan_x4plus', scales=[4], precisions=['auto', 'fp16', 'fp32'],
                settings_schema=dict(device=['auto', 'cuda', 'cpu'], precision=['auto', 'fp16', 'fp32'],
                                     format=['PNG', 'JPEG'], tile_size=dict(auto=0, minimum=32, maximum=2048)))

def load(device, precision):
    global MODEL, MODEL_KEY
    if device not in ('auto', 'cuda', 'cpu') or precision not in ('auto', 'fp16', 'fp32'):
        raise ValueError('장치 또는 정밀도 설정이 올바르지 않습니다.')
    if device == 'cuda' and not torch.cuda.is_available():
        raise RuntimeError('CUDA를 사용할 수 없습니다. CPU 모드를 선택하세요.')
    selected = ('cuda' if torch.cuda.is_available() else 'cpu') if device == 'auto' else device
    half = selected == 'cuda' and precision != 'fp32'
    key = (selected, half)
    if MODEL_KEY != key or MODEL is None:
        MODEL = None
        MODEL_KEY = None
        gc.collect()
        if torch.cuda.is_available():
            torch.cuda.empty_cache()
        path = ROOT / 'models/RealESRGAN_x4plus.pth'
        expected = (ROOT / 'models/SHA256SUMS').read_text(encoding='utf-8-sig').split()[0]
        if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise RuntimeError('모델 체크섬이 일치하지 않습니다.')
        net = RRDBNet(num_in_ch=3, num_out_ch=3, num_feat=64, num_block=23, num_grow_ch=32, scale=4)
        state = torch.load(path, map_location='cpu', weights_only=True)
        net.load_state_dict(state.get('params_ema', state.get('params')), strict=True)
        net = net.eval().to(selected)
        if half:
            net = net.half()
        MODEL, MODEL_KEY = net, key
    return MODEL, selected, half

def check_cancel(cancel):
    if cancel.is_set():
        raise InterruptedError('작업이 취소되었습니다.')

def check_space(dest, width, height):
    # Conservative uncompressed RGBA bound plus container overhead, checked before inference.
    needed = width * height * 16 * 4 + 1024 * 1024
    if shutil.disk_usage(dest).free < needed:
        raise OSError('출력 드라이브의 여유 공간이 부족합니다.')

@torch.inference_mode()
def infer(rgb, model, device, half, tile, job, cancel):
    h, w = rgb.shape[:2]
    result = np.empty((h * 4, w * 4, 3), dtype=np.uint8)
    total = ((h + tile - 1) // tile) * ((w + tile - 1) // tile)
    done = 0
    for y in range(0, h, tile):
        for x in range(0, w, tile):
            check_cancel(cancel)
            y1, x1 = max(0, y - 16), max(0, x - 16)
            y2, x2 = min(h, y + tile + 16), min(w, x + tile + 16)
            t = torch.from_numpy(rgb[y1:y2, x1:x2].copy()).permute(2, 0, 1).unsqueeze(0)
            t = t.to(device=device, dtype=torch.float16 if half else torch.float32) / 255
            out = model(t).squeeze(0).float().clamp_(0, 1).cpu().permute(1, 2, 0).numpy()
            oh, ow = min(tile, h - y) * 4, min(tile, w - x) * 4
            result[y * 4:y * 4 + oh, x * 4:x * 4 + ow] = np.rint(
                out[(y-y1)*4:(y-y1)*4+oh, (x-x1)*4:(x-x1)*4+ow] * 255).astype(np.uint8)
            del t, out
            check_cancel(cancel)
            done += 1
            emit('progress', job, stage='inference', progress=done * 100 / total)
    return Image.fromarray(result)

class RealEsrganEngine(BaseEngine):
    engine_id = 'realesrgan_x4plus'
    scale = 4

    def load(self, device, precision):
        return load(device, precision)

    def infer(self, rgb, model, device, half, tile, job, cancel):
        return infer(rgb, model, device, half, tile, job, cancel)

ENGINE = RealEsrganEngine()

def upscale(req, cancel):
    job = req['job_id']
    temp = None
    try:
        check_cancel(cancel)
        tile = int(req.get('tile_size', 0)) or 256
        if tile < 32 or tile > 2048:
            raise ValueError('타일 크기는 0(자동) 또는 32~2048이어야 합니다.')
        fmt = req.get('format', 'PNG').upper()
        if fmt not in ('PNG', 'JPEG'):
            raise ValueError('출력 형식은 PNG 또는 JPEG이어야 합니다.')
        if req.get('scale', 4) != 4 or req.get('engine', 'realesrgan_x4plus') != 'realesrgan_x4plus':
            raise ValueError('Real-ESRGAN x4plus의 4배 확대만 지원합니다.')
        emit('progress', job, stage='decoding', progress=None)
        image = read_image(req['input'])
        dest = Path(req['output_dir']).resolve()
        dest.mkdir(parents=True, exist_ok=True)
        check_space(dest, image.width, image.height)
        candidate = dest / ('.upscaler-' + job + '.tmp')
        # Probe write permission without ever overwriting an existing file.
        with candidate.open('xb'):
            pass
        temp = candidate
        check_cancel(cancel)
        emit('progress', job, stage='loading', progress=None)
        model, device, half = ENGINE.load(req.get('device', 'auto'), req.get('precision', 'auto'))
        check_cancel(cancel)
        alpha = image.getchannel('A') if image.mode == 'RGBA' else None
        rgb = np.array(image.convert('RGB'))
        for attempt in range(4):
            try:
                output = ENGINE.infer(rgb, model, device, half, tile, job, cancel)
                break
            except torch.cuda.OutOfMemoryError:
                if tile <= 32 or attempt == 3:
                    raise RuntimeError('GPU 메모리가 부족합니다. 더 작은 이미지나 CPU 모드를 사용하세요.') from None
            # Release the exception traceback and tile tensors before clearing the allocator.
            gc.collect()
            torch.cuda.empty_cache()
            tile = max(32, tile // 2)
            emit('progress', job, stage='retry', progress=None, tile_size=tile)
        check_cancel(cancel)
        if alpha is not None:
            output.putalpha(alpha.resize(output.size, Image.Resampling.BILINEAR))
        if fmt == 'JPEG' and output.mode == 'RGBA':
            bg = Image.new('RGB', output.size, 'white')
            bg.paste(output, mask=output.getchannel('A'))
            output = bg
        check_space(dest, image.width, image.height)
        emit('progress', job, stage='saving', progress=None)
        with temp.open('wb') as stream:
            output.save(stream, format=fmt, quality=95)
            stream.flush()
            os.fsync(stream.fileno())
        check_cancel(cancel)
        stem = Path(req['input']).stem[:120] + '_x4'
        suffix = '.jpg' if fmt == 'JPEG' else '.png'
        n = 0
        while True:
            check_cancel(cancel)
            target = dest / (stem + ('' if n == 0 else f'_{n:03d}') + suffix)
            try:
                # Windows same-volume rename refuses existing destinations, including symlinks.
                os.rename(temp, target)
                break
            except FileExistsError:
                n += 1
        temp = None
        return 'completed', dict(output=str(target), output_width=output.width, output_height=output.height,
                                 device=device, precision='fp16' if half else 'fp32')
    finally:
        if temp is not None:
            temp.unlink(missing_ok=True)

def work():
    global ACTIVE
    while True:
        task = QUEUE.get()
        if task is None:
            return
        req, cancel = task
        try:
            if req['type'] == 'load_model':
                check_cancel(cancel)
                _, device, half = load(req.get('device', 'auto'), req.get('precision', 'auto'))
                check_cancel(cancel)
                kind, data = 'completed', dict(device=device, precision='fp16' if half else 'fp32')
            else:
                kind, data = upscale(req, cancel)
        except InterruptedError as error:
            kind, data = 'cancelled', dict(message=str(error))
        except Exception as error:
            traceback.print_exc(file=sys.stderr)
            kind, data = 'failed', dict(message=str(error), code=type(error).__name__)
        # All cleanup has finished before the terminal event is published.
        with STATE_LOCK:
            ACTIVE = None
            emit(kind, req['job_id'], **data)

def main():
    global ACTIVE
    sys.stdin.reconfigure(encoding='utf-8')
    sys.stdout.reconfigure(encoding='utf-8')
    thread = threading.Thread(target=work, daemon=True)
    thread.start()
    emit('ready', **device_info())
    for line in sys.stdin:
        job = ''
        try:
            req = json.loads(line)
            job = req.get('job_id', '')
            kind = req.get('type')
            if req.get('protocol_version') != 1:
                raise ValueError('지원하지 않는 프로토콜 버전')
            if kind in ('hello', 'get_devices'):
                emit('device_info', job, **device_info())
            elif kind == 'cancel':
                with STATE_LOCK:
                    if ACTIVE and ACTIVE[0]['job_id'] == job:
                        ACTIVE[1].set()
            elif kind == 'shutdown':
                break
            elif kind in ('upscale', 'load_model'):
                if not isinstance(job, str) or not re.fullmatch(r'[A-Za-z0-9_-]{1,80}', job):
                    raise ValueError('job_id가 올바르지 않습니다.')
                with STATE_LOCK:
                    if ACTIVE is not None:
                        raise ValueError('Worker가 이미 작업 중입니다.')
                    ACTIVE = (req, threading.Event())
                    QUEUE.put_nowait(ACTIVE)
            else:
                raise ValueError('알 수 없는 명령')
        except Exception as error:
            emit('failed', job, message=str(error), code=type(error).__name__)
    with STATE_LOCK:
        if ACTIVE:
            ACTIVE[1].set()
    QUEUE.put(None)
    thread.join(timeout=15)
    if thread.is_alive():
        os._exit(1)

if __name__ == '__main__':
    main()
