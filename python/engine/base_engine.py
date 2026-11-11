from abc import ABC, abstractmethod

class BaseEngine(ABC):
    """Structural extension point; no plugin discovery or additional engines."""
    engine_id: str
    scale: int

    @abstractmethod
    def load(self, device, precision):
        raise NotImplementedError

    @abstractmethod
    def infer(self, rgb, model, device, half, tile, job, cancel):
        raise NotImplementedError
