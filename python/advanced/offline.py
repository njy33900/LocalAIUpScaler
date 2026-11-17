"""Fail closed: inference must never fetch assets or transmit input."""
import os
import socket

def enforce():
    for key in ('HF_HUB_OFFLINE', 'TRANSFORMERS_OFFLINE', 'HF_DATASETS_OFFLINE'):
        os.environ[key] = '1'
    def deny(*args, **kwargs):
        raise RuntimeError('NETWORK_DISABLED: install all assets before inference')
    socket.create_connection = deny
    socket.socket.connect = deny
    socket.socket.connect_ex = deny
