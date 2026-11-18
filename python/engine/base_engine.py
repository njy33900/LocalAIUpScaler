from abc import ABC, abstractmethod


class BaseEngine(ABC):
    """Structural extension point; no plugin discovery or additional engines."""

    engine_id: str
    scale: int

    @abstractmethod
    def load(self, device, precision):
        """장치·정밀도 설정에 맞는 모델과 실행 정보를 준비하는 구현 계약."""
        raise NotImplementedError

    @abstractmethod
    def infer(self, rgb, model, device, half, tile, job, cancel):
        """진행 알림과 취소 경계를 지키면서 입력 RGB를 복원하는 구현 계약."""
        raise NotImplementedError
