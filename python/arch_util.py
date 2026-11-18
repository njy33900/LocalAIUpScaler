# Adaptation helpers for BasicSR RRDBNet v1.4.2 (Apache-2.0).
"""BasicSR 호환 RRDB 계층의 초기화와 공간 재배치 보조 함수."""

import torch
from torch import nn


@torch.no_grad()
def default_init_weights(modules, scale=1):
    """합성곱·선형 계층을 초기화하고 잔차 경로의 가중치 크기를 조절한다.

    Args:
        modules: 하위 계층까지 초기화할 모듈 목록.
        scale: Kaiming 초기화 후 가중치에 곱할 잔차 스케일.
    """

    for root in modules:
        for module in root.modules():
            if isinstance(module, (nn.Conv2d, nn.Linear)):
                nn.init.kaiming_normal_(module.weight, a=0, mode="fan_in")
                module.weight.mul_(scale)

                if module.bias is not None:
                    module.bias.zero_()


def make_layer(block, count, **kwargs):
    """같은 블록을 지정한 개수만큼 독립 생성해 Sequential로 묶는다."""

    return nn.Sequential(*(block(**kwargs) for _ in range(count)))


def pixel_unshuffle(x, scale):
    """공간 축을 채널로 재배치해 저배율 RRDB 입력 형태를 맞춘다."""

    return torch.nn.functional.pixel_unshuffle(x, scale)
