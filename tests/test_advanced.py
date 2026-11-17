import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import socket
import sys
import tempfile
import unittest
from unittest.mock import patch

ADVANCED=Path(__file__).resolve().parents[1]/'python/advanced'
sys.path.append(str(ADVANCED))
spec=importlib.util.spec_from_file_location('advanced_worker',ADVANCED/'worker.py')
worker=importlib.util.module_from_spec(spec);spec.loader.exec_module(worker)
import catalog
import offline

def request():
    return dict(protocol_version=1,type='restore',job_id='test-1',engine='diffbir_v21',device='cuda',scale=2,
                input=str(Path(__file__).resolve()),output_dir=str(ADVANCED),format='PNG',options={})

class ContractTests(unittest.TestCase):
    def test_defaults_and_random_seed(self):
        req=worker.validate(request())
        self.assertEqual(req['options']['steps'],10)
        self.assertGreaterEqual(req['options']['seed'],0)
    def test_fixed_seed(self):
        req=request();req['options']={'seed':123};self.assertEqual(worker.validate(req)['options']['seed'],123)
    def test_unknown_options(self):
        req=request();req['options']={'unsupported':1}
        with self.assertRaises(ValueError): worker.validate(req)
    def test_invalid_parameters(self):
        for key,value in [('scale',3),('engine','unknown'),('device','cpu'),('job_id','../outside'),('format','GIF'),('task','face')]:
            with self.subTest(key=key):
                req=request();req[key]=value
                with self.assertRaises(ValueError): worker.validate(req)
    def test_invalid_numeric_options(self):
        for options in [{'steps':0},{'steps':2.5},{'cfg':float('nan')},{'seed':True},{'tiled':'yes'},{'seed':-2}]:
            req=request();req['options']=options
            with self.subTest(options=options), self.assertRaises(ValueError): worker.validate(req)
    def test_engine_specific_options(self):
        req=request();req['options']={'strength':1.5}
        with self.assertRaises(ValueError): worker.validate(req)
        req=request();req['engine']='supir_v0f';req['options']={'noise_aug':1}
        with self.assertRaises(ValueError): worker.validate(req)
    def test_missing_model_not_ready(self):
        with patch.object(catalog,'load_config',return_value={}):
            status=catalog.inspect('supir_v0q')
            self.assertFalse(status['verified_inference']);self.assertGreater(len(status['missing']),3)
    def test_corrupt_asset_hash_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            path=Path(temp)/'asset';path.write_bytes(b'corrupt')
            assets={k:dict(path=str(path),sha256='0'*64) for k in ('cleaner','sd','control')}
            config={'diffbir_v21':dict(python=sys.executable,upstream=temp,assets=assets)}
            with patch.object(catalog,'load_config',return_value=config):
                self.assertTrue(any('SHA256 mismatch' in s for s in catalog.inspect('diffbir_v21',deep=True)['missing']))
    def test_staging_cleanup_on_failure(self):
        with tempfile.TemporaryDirectory() as temp:
            req=request();req['output_dir']=temp
            directory=Path(temp)/('.restoration-'+req['job_id'])
            with self.assertRaisesRegex(RuntimeError,'injected'):
                with worker.staging_directory(req) as work:
                    (work/'partial.png').write_bytes(b'partial')
                    raise RuntimeError('injected')
            self.assertFalse(directory.exists())
    def test_staging_does_not_reuse_existing_directory(self):
        with tempfile.TemporaryDirectory() as temp:
            req=request();req['output_dir']=temp
            directory=Path(temp)/('.restoration-'+req['job_id']);directory.mkdir()
            marker=directory/'keep';marker.write_bytes(b'keep')
            with self.assertRaises(FileExistsError):
                with worker.staging_directory(req): pass
            self.assertEqual(marker.read_bytes(),b'keep')
    def test_network_connections_denied(self):
        with patch.object(socket,'create_connection'),patch.object(socket.socket,'connect'),patch.object(socket.socket,'connect_ex'):
            offline.enforce()
            with self.assertRaisesRegex(RuntimeError,'NETWORK_DISABLED'):
                socket.create_connection(('example.com',443))
            with socket.socket() as sock:
                with self.assertRaisesRegex(RuntimeError,'NETWORK_DISABLED'):
                    sock.connect(('127.0.0.1',11434))

if __name__=='__main__': unittest.main()
