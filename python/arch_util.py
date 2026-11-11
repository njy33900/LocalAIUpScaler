# Adaptation helpers for BasicSR RRDBNet v1.4.2 (Apache-2.0).
import torch
from torch import nn

@torch.no_grad()
def default_init_weights(modules, scale=1):
    for root in modules:
        for module in root.modules():
            if isinstance(module, (nn.Conv2d, nn.Linear)):
                nn.init.kaiming_normal_(module.weight, a=0, mode='fan_in')
                module.weight.mul_(scale)
                if module.bias is not None:
                    module.bias.zero_()

def make_layer(block, count, **kwargs):
    return nn.Sequential(*(block(**kwargs) for _ in range(count)))

def pixel_unshuffle(x, scale):
    return torch.nn.functional.pixel_unshuffle(x, scale)
