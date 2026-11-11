import hashlib
import json
import queue
import subprocess
import sys
import tempfile
import threading
import unittest
from pathlib import Path
from unittest.mock import patch
from collections import namedtuple

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'python'))
import worker
from imaging import read_image

class Client:
    def __init__(self):
        self.process = subprocess.Popen([sys.executable, '-u', str(ROOT/'python/worker.py')],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8')
        self.messages = queue.Queue()
        self.errors = []
        self.out = threading.Thread(target=self.read, daemon=True); self.out.start()
        self.err = threading.Thread(target=lambda: self.errors.extend(self.process.stderr.readlines()), daemon=True); self.err.start()
        self.ready = self.next()
    def read(self):
        for line in self.process.stdout:
            try: self.messages.put(json.loads(line))
            except Exception as error: self.messages.put(error)
    def next(self):
        value = self.messages.get(timeout=90)
        if isinstance(value, Exception): raise value
        return value
    def send(self, **request):
        self.process.stdin.write(json.dumps(dict(protocol_version=1, **request))+'\n'); self.process.stdin.flush()
    def terminal(self):
        while True:
            value = self.next()
            if value['type'] in ('completed', 'failed', 'cancelled'): return value
    def close(self):
        try:
            self.send(type='shutdown', job_id='')
            self.process.wait(timeout=20)
        finally:
            if self.process.poll() is None: self.process.kill(); self.process.wait()
            self.out.join(timeout=5); self.err.join(timeout=5)
            self.process.stdin.close(); self.process.stdout.close(); self.process.stderr.close()

class WorkerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls): cls.client = Client()
    @classmethod
    def tearDownClass(cls): cls.client.close()
    def test_cpu_and_cuda_collision_original_and_alpha(self):
        with tempfile.TemporaryDirectory(prefix='upscaler 한글 ') as folder:
            path=Path(folder)/'source.png'; Image.new('RGBA',(8,6),(100,150,200,128)).save(path)
            before=hashlib.sha256(path.read_bytes()).hexdigest(); outputs=[]
            devices=['cpu','cuda'] if self.client.ready['cuda'] else ['cpu','cpu']
            for i,device in enumerate(devices):
                self.client.send(type='upscale',job_id=f'alpha{i}',input=str(path),output_dir=folder,device=device,precision='fp16',tile_size=32)
                response=self.client.terminal(); self.assertEqual(response['type'],'completed',response)
                self.assertEqual(response['precision'],'fp32' if device=='cpu' else 'fp16')
                outputs.append(response['output'])
                with Image.open(response['output']) as result:
                    self.assertEqual(result.size,(32,24)); self.assertEqual(result.mode,'RGBA')
                    self.assertEqual(result.getchannel('A').getextrema(),(128,128))
            self.assertNotEqual(outputs[0],outputs[1]); self.assertEqual(before,hashlib.sha256(path.read_bytes()).hexdigest())
    def test_cancel_at_tile_boundary_and_reuse(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'cancel.png'; Image.new('RGB',(128,128),'red').save(path)
            self.client.send(type='upscale',job_id='cancel-test',input=str(path),output_dir=folder,device='cpu',tile_size=32)
            while True:
                event=self.client.next()
                if event['type']=='progress' and event['stage']=='inference': break
                self.assertNotEqual(event['type'],'failed',event)
            self.client.send(type='cancel',job_id='cancel-test')
            self.assertEqual(self.client.terminal()['type'],'cancelled')
            self.assertEqual(list(Path(folder).glob('.upscaler-*')),[])
            self.assertFalse((Path(folder)/'cancel_x4.png').exists())
            Image.new('RGB',(4,4),'red').save(path)
            self.client.send(type='upscale',job_id='reuse',input=str(path),output_dir=folder,device='cpu')
            self.assertEqual(self.client.terminal()['type'],'completed')
    def test_malformed_and_failure_recovery(self):
        self.client.process.stdin.write('not-json\n'); self.client.process.stdin.flush()
        self.assertEqual(self.client.terminal()['type'],'failed')
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'bad.png'; path.write_bytes(b'broken')
            self.client.send(type='upscale',job_id='bad',input=str(path),output_dir=folder)
            response=self.client.terminal(); self.assertEqual(response['type'],'failed'); self.assertEqual(response['job_id'],'bad')
            self.client.send(type='get_devices',job_id='device-query')
            self.assertEqual(self.client.next()['type'],'device_info')
    def test_cuda_fp32_and_jpeg(self):
        if not self.client.ready['cuda']: self.skipTest('CUDA unavailable')
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'transparent.png'; Image.new('RGBA',(4,3),(0,0,0,0)).save(path)
            self.client.send(type='upscale',job_id='jpeg',input=str(path),output_dir=folder,device='cuda',precision='fp32',format='JPEG')
            response=self.client.terminal(); self.assertEqual(response['type'],'completed',response); self.assertEqual(response['precision'],'fp32')
            with Image.open(response['output']) as image:
                self.assertEqual(image.size,(16,12)); self.assertEqual(image.getpixel((0,0)),(255,255,255))

class PolicyTests(unittest.TestCase):
    def test_disk_space_rejected(self):
        Usage=namedtuple('Usage','total used free')
        with patch('worker.shutil.disk_usage',return_value=Usage(10,10,0)):
            with self.assertRaises(OSError): worker.check_space('.',10,10)
    def test_decode_all_formats_and_orientation(self):
        with tempfile.TemporaryDirectory() as folder:
            for extension in ['png','jpg','bmp','tiff','webp']:
                path=Path(folder)/('sample.'+extension); Image.new('RGB',(9,5),'blue').save(path)
                self.assertEqual(read_image(path).size,(9,5))
            path=Path(folder)/'rotated.jpg'; exif=Image.Exif(); exif[274]=6
            Image.new('RGB',(9,5),'blue').save(path,exif=exif)
            self.assertEqual(read_image(path).size,(5,9))
    def test_mismatch_and_16bit(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'wrong.png'; Image.new('RGB',(2,2)).save(path,format='JPEG')
            with self.assertRaises(ValueError): read_image(path)
            path=Path(folder)/'gray.tiff'; Image.fromarray(np.array([[0,32768,65535]],dtype=np.uint16)).save(path)
            self.assertEqual(list(read_image(path).get_flattened_data()),[(0,0,0),(128,128,128),(255,255,255)])
    def test_tiff_first_page(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'pages.tiff'; Image.new('RGB',(3,5)).save(path,save_all=True,append_images=[Image.new('RGB',(6,7))])
            self.assertEqual(read_image(path).size,(3,5))
    def test_oom_retry_bounded_and_temp_cleanup(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'small.png'; Image.new('RGB',(2,2)).save(path)
            request=dict(job_id='oom',input=str(path),output_dir=folder)
            with patch('worker.load',return_value=(None,'cuda',True)), patch('worker.infer',side_effect=worker.torch.cuda.OutOfMemoryError()), patch('worker.emit'), patch('worker.torch.cuda.empty_cache'), patch('worker.gc.collect'):
                with self.assertRaises(RuntimeError): worker.upscale(request,threading.Event())
                self.assertEqual(worker.infer.call_count,4)
            self.assertEqual(list(Path(folder).glob('.upscaler-*')),[])
    def test_cancel_before_commit_removes_temp(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'small.png'; Image.new('RGB',(2,2)).save(path); cancel=threading.Event()
            def progress(kind,job,**data):
                if data.get('stage')=='saving': cancel.set()
            with patch('worker.load',return_value=(None,'cpu',False)),patch('worker.infer',return_value=Image.new('RGB',(8,8))),patch('worker.emit',side_effect=progress):
                with self.assertRaises(InterruptedError): worker.upscale(dict(job_id='saving',input=str(path),output_dir=folder),cancel)
            self.assertFalse((Path(folder)/'small_x4.png').exists());self.assertEqual(list(Path(folder).glob('.upscaler-*')),[])
    def test_preexisting_temp_is_not_deleted(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'small.png';Image.new('RGB',(2,2)).save(path)
            existing=Path(folder)/'.upscaler-existing.tmp';existing.write_bytes(b'keep')
            with self.assertRaises(FileExistsError): worker.upscale(dict(job_id='existing',input=str(path),output_dir=folder),threading.Event())
            self.assertEqual(existing.read_bytes(),b'keep')

if __name__=='__main__': unittest.main()
